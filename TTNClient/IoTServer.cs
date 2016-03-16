using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Newtonsoft.Json;
using IoT.Protocol;

namespace IoT
{
	public enum PACKET_TYPE : byte
	{
		PKT_PUSH_DATA = 0,
		PKT_PUSH_ACK = 1,
		PKT_PULL_DATA = 2,
		PKT_PULL_RESP = 3,
		PKT_PULL_ACK = 4,
		PKT_TX_ACK = 5,
		PKT_UNKNOWN
	}

	public enum SERVER_STATE
	{
		Initialize,
		Running,
		Stoped
	}

	public class GatewayState
	{
		public UInt64 EUI;
		public DateTime LastTime;
		public EndPoint EndPoint;
		public GatewayState()
		{ }

		public override string ToString()
		{
			return (string.Format(
				"{{Time:{0}, EUI:{1:X8}, Remote:{2}}}",
				LastTime,
				EUI,
				EndPoint
				));
		}
	}

	public enum PULL_RESP_STATE
	{
		Initialize,
		Ready,
		Sended
	}
	public class GatewayPullResp
	{
		public PULL_RESP_STATE State;
		public DateTime TimeToSend;
		public string Error;
		public int Retry;
		public GatewayTxpkV1 Txpk;
		public GatewayPullResp()
		{
			State = PULL_RESP_STATE.Initialize;
		}
	}

	public class ServerState
	{
		public UInt32 PullRespToken;
		public Dictionary<UInt32, GatewayPullResp> PullRespPackets;
		public object LockPullResp;
		public Socket Socket;
		public SERVER_STATE State;
		public Queue<GatewayPacket> Packets;
		public object LockPackets;
		public int LostPackets;
		public Exception ErrorSendToCB;
		public Exception ErrorSendTo;
		public Exception ErrorReceiveFromCB;
		public Exception ErrorReceiveFrom;
		public Dictionary<UInt64, GatewayState> Gateways;
		public byte[] Buffer = new byte[2500];
		public ManualResetEvent ResetEvent;

		public ServerState()
		{
			State = SERVER_STATE.Initialize;
			LockPackets = new object();
			LockPullResp = new object();
			PullRespToken = 0;
			PullRespPackets = new Dictionary<uint, GatewayPullResp>(100);
			Packets = new Queue<GatewayPacket>(100);
			Gateways = new Dictionary<ulong, GatewayState>(100);
			ResetEvent = new ManualResetEvent(false);
		}
	}

	public class IoTServer
	{
		public ServerState state = new ServerState();

		public IoTServer()
		{
		}

		public void Run()
		{
			Console.WriteLine("\nPress any key to exit");

			JsonSerializerSettings settings = new JsonSerializerSettings();
			settings.Culture = CultureInfo.InvariantCulture;

			try
			{
				state.Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
				state.Socket.Bind(new IPEndPoint(IPAddress.Any, Properties.Settings.Default.LwgPort));

				IPEndPoint ipep = new IPEndPoint(IPAddress.Any, 0);
				EndPoint epSender = (EndPoint)ipep;
				state.Socket.BeginReceiveFrom(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, ref epSender, new AsyncCallback(ReceiveFromCallback), state);
				state.State = SERVER_STATE.Running;

				/*
				 * Test PULL_RESP
				 */
				GatewayPullResp TestPullRest = new GatewayPullResp();
				{
					GatewayTxpkV1 txpk = new GatewayTxpkV1();
					txpk.imme = true;
					txpk.tmst = 0;
					txpk.time = DateTime.UtcNow;
					txpk.freq = 864.123456;
					txpk.rfch = 0;
					txpk.powe = 14;
					txpk.modu = "SF11BW125";
					txpk.codr = "4/6";
					txpk.ipol = false;
					txpk.size = 32;
					txpk.data = "H3P3N2i9qc4yt7rK7ldqoeCVJGBybzPY5h1Dd7P7p8v";

					TestPullRest.Txpk = txpk;
				}

				DateTime keyTime = DateTime.Now;
				while (state.State == SERVER_STATE.Running)
				{
					while (state.Packets.Count > 0)
					{
						#region Process packets
						GatewayPacket packet = null;
						lock (state.LockPackets)
						{
							packet = state.Packets.Dequeue();
							state.ResetEvent.Reset();
						}

						if (packet == null)
							continue;

						Console.WriteLine(string.Format("Recv from {0}", packet.Sender));
						Console.WriteLine(packet.ToString());

						try
						{
							switch (packet.PacketType)
							{
								case PACKET_TYPE.PKT_TX_ACK:
									UInt32 token = packet.Token;
									GatewayTxAck data =
										string.IsNullOrEmpty(packet.Json)
										? null
										: JsonConvert.DeserializeObject<GatewayTxAck>(packet.Json, settings);

									if (data != null)
									{	// Error
										Console.WriteLine("ERROR:");
										Console.WriteLine(JsonConvert.SerializeObject(data, Formatting.Indented));

										if (state.PullRespPackets.ContainsKey(token))
										{
											lock (state.LockPullResp)
											{
												GatewayPullResp pull = state.PullRespPackets[token];
												if (pull.Retry > 0)
												{
													pull.Retry--;
													pull.TimeToSend = DateTime.Now.AddSeconds(15);
													pull.State = PULL_RESP_STATE.Ready;
												}
												else
												{
													state.PullRespPackets.Remove(token);
												}
											}
										}
									}
									else
									{	// No error
										if (state.PullRespPackets.ContainsKey(token))
											lock (state.LockPullResp)
												state.PullRespPackets.Remove(token);
									}
									break;

								case PACKET_TYPE.PKT_PULL_DATA:
									GatewayState gw;
									UInt64 eui = BitConverter.ToUInt64(packet.EUI, 0);
									if (state.Gateways.ContainsKey(eui))
										gw = state.Gateways[eui];
									else
										gw = new GatewayState();

									gw.EndPoint = packet.Sender;
									gw.LastTime = DateTime.Now;
									SendTo(state, packet);
									break;

								case PACKET_TYPE.PKT_PUSH_DATA:
									if (string.IsNullOrEmpty(packet.Json))
									{
										Console.WriteLine("JSON ERROR: Empty payload");
									}
									else
									{
										GatewayPushData json =
											(packet.Version == 1)
											? (GatewayPushData)JsonConvert.DeserializeObject<GatewayPushDataV1>(packet.Json, settings)
											: (GatewayPushData)JsonConvert.DeserializeObject<GatewayPushDataV2>(packet.Json, settings)
											;
										if (json != null)
										{
											/*
											Console.WriteLine("JSON:");
											Console.WriteLine(JsonConvert.SerializeObject(json, Formatting.Indented));
											*/
											bool success = true;
											if (json.stat != null)
												success &= ProcessingStat(json.stat);

											if (json.rxpk != null && json.rxpk.Count > 0)
												for (int idx = 0; idx < json.rxpk.Count; idx++)
													success &= ProcessingRxpt(json.rxpk[idx]);
											if (success)
											{
												// Thread.Sleep(10);
												SendTo(state, packet);
											}
										}
									}
									break;
								default:
									break;
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine(string.Format("Processing error:\n{0}", ex.ToString()));
						}
						#endregion
					}

					lock (state.LockPackets)
						if (state.Packets.Count == 0)
							state.ResetEvent.Reset();

					if (!state.ResetEvent.WaitOne(500))
					{
						if ((DateTime.Now - keyTime).Seconds >= 5)
						{
							keyTime = DateTime.Now;
							if (Console.KeyAvailable)
							{
								while (Console.KeyAvailable)
									Console.ReadKey(true);
								state.State = SERVER_STATE.Stoped;
								break;
							}
						}
					}
				}

				state.Socket.Shutdown(SocketShutdown.Both);
				state.Socket.Close();
			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
			}
		}

		private bool ProcessingRxpt(GatewayRxpk gatewayRxpk)
		{
			return true;
		}

		private bool ProcessingStat(GatewayStat gatewayStat)
		{
			return true;
		}

		private void ReceiveFromCallback(IAsyncResult ar)
		{
			ServerState state = (ServerState)ar.AsyncState;
			if (state.State == SERVER_STATE.Running)
			{
				Socket socket = state.Socket;
				try
				{
					// Read packet from gateway
					IPEndPoint ipep = new IPEndPoint(IPAddress.Any, 0);
					EndPoint epSender = (EndPoint)ipep;
					int bytesRead = socket.EndReceiveFrom(ar, ref epSender);
					byte[] buffer = state.Buffer;

					// Check packet from gateway
					if (bytesRead >= 4)
					{
						byte version = buffer[0];
						if (version == 1 || version == 2)
						{
							GatewayPacket packet = new GatewayPacket();
							packet.Version = version;
							packet.Token = BitConverter.ToUInt16(buffer, 1);

							byte type = buffer[3];
							switch (type)
							{
								case (byte)PACKET_TYPE.PKT_PUSH_DATA:
									if (bytesRead > 12)
									{
										packet.Json = Encoding.ASCII.GetString(buffer, 12, bytesRead - 12);
										packet.SetEUI(buffer);
									}
									else
										packet = null;
									break;
								case (byte)PACKET_TYPE.PKT_PULL_DATA:
									if (bytesRead == 12)
										packet.SetEUI(buffer);
									else
										packet = null;
									break;
								case (byte)PACKET_TYPE.PKT_TX_ACK:
									if (bytesRead > 4)
										packet.Json = Encoding.ASCII.GetString(buffer, 4, bytesRead - 4);
									else
										packet = null;
									break;
								default:
									packet = null;
									break;
							}

							if (packet != null)
							{
								packet.PacketType = (PACKET_TYPE)type;
								packet.Sender = epSender;

								lock (state.LockPackets)
								{
									if (state.Packets.Count < 100)
										state.Packets.Enqueue(packet);
									else
										state.LostPackets++;

									state.ResetEvent.Set();
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					state.ErrorReceiveFromCB = ex;
				}

				try
				{
					IPEndPoint ipep = new IPEndPoint(IPAddress.Any, 0);
					EndPoint epSender = (EndPoint)ipep;
					socket.BeginReceiveFrom(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, ref epSender, new AsyncCallback(ReceiveFromCallback), state);
				}
				catch(Exception ex)
				{
					state.ErrorReceiveFrom = ex;
				}
			}
		}

		private void SendTo(ServerState state, GatewayPacket packet)
		{
			if (state.State == SERVER_STATE.Running)
			{
				byte[] data = null;
				PACKET_TYPE type = PACKET_TYPE.PKT_UNKNOWN;

				switch (packet.PacketType)
				{
					case PACKET_TYPE.PKT_PUSH_DATA:
						data = new byte[4];
						type = PACKET_TYPE.PKT_PUSH_ACK;
						break;
					case PACKET_TYPE.PKT_PULL_DATA:
						data = new byte[12];
						packet.EUI.CopyTo(data, 4);
						type = PACKET_TYPE.PKT_PULL_ACK;
						break;
					default:
						break;
				}

				if (type != PACKET_TYPE.PKT_UNKNOWN)
				{
					data[0] = packet.Version;
					BitConverter.GetBytes(packet.Token).CopyTo(data, 1);
					data[3] = (byte)type;

					try
					{
						state.Socket.BeginSendTo(data, 0, data.Length, SocketFlags.None, packet.Sender, new AsyncCallback(SendToCallback), state);
					}
					catch (Exception ex)
					{
						state.ErrorSendTo = ex;
					}
				}
			}
		}

		private void SendToCallback(IAsyncResult ar)
		{
			ServerState state = (ServerState)ar.AsyncState;
			if (state.State == SERVER_STATE.Running)
			{
				try
				{
					state.Socket.EndSendTo(ar);
				}
				catch (Exception ex)
				{
					state.ErrorSendToCB = ex;
				}
			}
		}
	}
}
