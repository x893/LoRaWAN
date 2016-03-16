using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

using RestSharp;
using Newtonsoft.Json;
using IoT.Protocol;


namespace IoT
{
	class Program
	{
		static bool complete;

		static void Main(string[] args)
		{
			UseMQTT();

			// UseREST();

			// IoTServer server = new IoTServer();
			// server.Run();

			WaitKeyPress();
			Environment.Exit(0);
		}

		#region client_MqttMsgPublishReceived
		static void client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
		{
			Console.WriteLine(string.Format(
				"------ PublishReceived\nTopic:{0} DupFlag:{1} QosLevel:{2} Retain:{3}",
				e.Topic,
				e.DupFlag,
				e.QosLevel,
				e.Retain
				));

			if (!string.IsNullOrEmpty(e.Topic) && e.Message != null && e.Message.Length > 0)
			{
				string msg = Encoding.UTF8.GetString(e.Message);
				try
				{
					if (string.IsNullOrEmpty(msg))
					{
						Console.WriteLine("MESSAGE: EMPTY");
					}
					else if (e.Topic.StartsWith("gateways/"))
					{
						ApiGatewayStatus status = JsonConvert.DeserializeObject<ApiGatewayStatus>(msg);
						Console.WriteLine(string.Format("STATUS: {0}", status));
					}
					else if (e.Topic.StartsWith("nodes/"))
					{
						ApiNodePacket packet = JsonConvert.DeserializeObject<ApiNodePacket>(msg);
						Console.WriteLine(string.Format("PACKET: {0}", packet));
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(string.Format("JSON ERROR: {0}", ex.Message));
					Console.WriteLine(string.Format("MESSAGE: {0}", msg));
				}
			}
			else
			{
				Console.WriteLine("ERROR: EMPTY MESSAGE");
			}
		}
		#endregion
		#region client_MqttMsgUnsubscribed
		static void client_MqttMsgUnsubscribed(object sender, MqttMsgUnsubscribedEventArgs e)
		{
			Console.WriteLine(string.Format(
				"------ Unsubscribed MsgId:{0}", e.MessageId
				));
			complete = true;
		}
		#endregion
		#region client_MqttMsgSubscribed
		static void client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
		{
			Console.WriteLine(string.Format(
				"------ Subscribed MsgId:{0}", e.MessageId
				));
			complete = true;
		}
		#endregion
		#region client_ConnectionClosed
		static void client_ConnectionClosed(object sender, EventArgs e)
		{
			Console.WriteLine(string.Format(
				"------ Connection Closed"
				));
			complete = true;
		}
		#endregion

		#region UseMQTT()
		private static void UseMQTT()
		{
			MqttClient mqtt = null;
			try
			{
				// Create M2MQTT client
				mqtt = new MqttClient(
					Properties.Settings.Default.MQTTServer,	// Server name
					Properties.Settings.Default.MQTTPort,	// Server port
					false,
					MqttSslProtocols.None,
					null,
					null
					);
				// Set protocol for server
				mqtt.ProtocolVersion = MqttProtocolVersion.Version_3_1;
				// Add event handlers
				mqtt.MqttMsgPublishReceived += client_MqttMsgPublishReceived;
				mqtt.MqttMsgSubscribed += client_MqttMsgSubscribed;
				mqtt.MqttMsgUnsubscribed += client_MqttMsgUnsubscribed;
				mqtt.ConnectionClosed += client_ConnectionClosed;

				Console.WriteLine(string.Format(
					"Connect to broker: {0}:{1}",
					Properties.Settings.Default.MQTTServer,
					Properties.Settings.Default.MQTTPort
					));
				// Subscribe to broker topics
				// 1. gateway messages
				// 2. nodes packets
				string[] topic = new string[Properties.Settings.Default.MQTTTopic.Count];
				byte[] qos = new byte[Properties.Settings.Default.MQTTTopic.Count];
				for (int i = 0; i < topic.Length; i++)
				{
					topic[i] = Properties.Settings.Default.MQTTTopic[i];
					Console.WriteLine(string.Format("Subscribe to {0}", topic[i]));
					qos[i] = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE;
				}
				// Connect to server
				mqtt.Connect(Guid.NewGuid().ToString());
				// and subscribe
				ushort msgId = mqtt.Subscribe(topic, qos);


				WaitKeyPress("Press any key to exit\n");

				// Unsubscribe
				mqtt.Unsubscribe(topic);
				complete = false;
				for (int timeout = 0; timeout < 500; timeout++)
				{
					if (complete)
						break;
					Thread.Sleep(10);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(string.Format("MQTT ERROR:{0}", ex.Message));
			}

			// Disconnect from server
			if (mqtt != null && mqtt.IsConnected)
			{
				mqtt.Disconnect();
				complete = false;
				for (int timeout = 0; timeout < 500; timeout++)
				{
					if (complete)
						break;
					Thread.Sleep(10);
				}
			}

			WaitKeyPress();
		}
		#endregion

		#region UseREST()
		private static void UseREST()
		{
			string server = Properties.Settings.Default.Server;
			string serverFormat = Properties.Settings.Default.ServerFormat;
			string gatewayUrl = Properties.Settings.Default.GatewayUrl;
			string nodeUrl = Properties.Settings.Default.NodeUrl;
			bool useDB = Properties.Settings.Default.UseDB;
			int limit = Properties.Settings.Default.Limit;

			Dictionary<string, Gateway> gw_list = new Dictionary<string, Gateway>();

			try
			{
				if (useDB)
				{
					// Load gateways list from database
					using (LgwDbContext ctx = new LgwDbContext())
					{
						List<Gateway> list = ctx.Database.SqlQuery<Gateway>(@"
SET NOCOUNT ON
SELECT * FROM [Gateway]
").ToList();
						gw_list = new Dictionary<string, Gateway>(list.Count);

						foreach (Gateway gw in list)
						{
							if (gw_list.ContainsKey(gw.eui))
							{	// Multiple gateway EUI found
								Console.WriteLine(string.Format("Duplicate gateway {0} in database", gw.eui));
								WaitKeyPress();
								Environment.Exit(1);
							}
							gw_list.Add(gw.eui, gw);
						}
					}

					/*
					// Load nodes list from database
					using (LgwDbContext ctx = new LgwDbContext())
					{
						List<Gateway> list = ctx.Database.SqlQuery<Gateway>(@"
SET NOCOUNT ON
SELECT * FROM [Gateway]
").ToList();
						gw_list = new Dictionary<string, Gateway>(list.Count);

						foreach (Gateway gw in list)
						{
							if (gw_list.ContainsKey(gw.eui))
							{	// Multiple gateway EUI found
								Console.WriteLine(string.Format("Duplicate gateway {0} in database", gw.eui));
								WaitKeyPress();
								Environment.Exit(1);
							}
							gw_list.Add(gw.eui, gw);
						}
					}
					*/
				}

				RestClient client = new RestClient(server);

				foreach (string eui in Properties.Settings.Default.NodeEUI)
				{
					Console.WriteLine(string.Format("Last {0} messages for nodes {1}", limit == 0 ? "all" : limit.ToString(), eui));

					RestRequest request = new RestRequest(nodeUrl, Method.GET);
					if (!string.IsNullOrEmpty(serverFormat))
						request.AddParameter("format", serverFormat);
					if (limit > 0)
						request.AddParameter("limit", limit);
					request.AddUrlSegment("eui", eui);

					IRestResponse<List<ApiNodePacket>> response = client.Execute<List<ApiNodePacket>>(request);

					if (response.ResponseStatus == ResponseStatus.Completed && response.Data != null)
					{
						foreach (ApiNodePacket data in response.Data)
						{
							if (useDB)
							{
								DBCheckGateway(gw_list, data.gateway_eui);
								// DBCheckNode(gw_list, status);
							}
							Console.WriteLine(data.ToString());
						}
					}
					else
					{
						Console.WriteLine("Error:");
						if (response.ErrorException != null)
							Console.WriteLine(response.ErrorException);
						Console.WriteLine(response.Content);
						break;
					}
				}

				foreach (string eui in Properties.Settings.Default.GatewayEUI)
				{
					Console.WriteLine(string.Format("Last {0} status messages for gateway {1}", limit == 0 ? "all" : limit.ToString(), eui));

					RestRequest request = new RestRequest(gatewayUrl, Method.GET);
					if (!string.IsNullOrEmpty(serverFormat))
						request.AddParameter("format", serverFormat);
					if (limit > 0)
						request.AddParameter("limit", limit);
					request.AddUrlSegment("eui", eui);

					IRestResponse<List<ApiGatewayStatus>> response = client.Execute<List<ApiGatewayStatus>>(request);

					if (response.ResponseStatus == ResponseStatus.Completed && response.Data != null)
					{
						foreach (ApiGatewayStatus status in response.Data)
						{
							if (useDB)
								DBCheckGateway(gw_list, status);
							Console.WriteLine(status.ToString());
						}
					}
					else
					{
						Console.WriteLine("Error:");
						if (response.ErrorException != null)
							Console.WriteLine(response.ErrorException);
						Console.WriteLine(response.Content);
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(string.Format("Error: {0}", ex.Message));
				WaitKeyPress();
				Environment.Exit(2);
			}
			WaitKeyPress();
			Environment.Exit(0);
		}
		#endregion

		#region Wait key with message
		/// <summary>
		/// 
		/// </summary>
		private static void WaitKeyPress()
		{
			WaitKeyPress("\nPress any key to continue ...");
		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="msg"></param>
		private static void WaitKeyPress(string msg)
		{
			Console.Write(msg);
			Console.ReadKey(true);
		}
		#endregion

		#region Check gateway in database
		private static void DBCheckGateway(Dictionary<string, Gateway> gw_list, string p)
		{

		}

		/// <summary>
		/// Add gateway to database
		/// </summary>
		/// <param name="gw_db_list"></param>
		/// <param name="status"></param>
		private static void DBCheckGateway(Dictionary<string, Gateway> gw_db_list, ApiGatewayStatus status)
		{
			if (!gw_db_list.ContainsKey(status.eui))
			{
				try
				{
					Console.WriteLine(string.Format("Add gateway {0} to database", status.eui));

					Gateway gw = new Gateway();
					gw.eui = status.eui;
					gw.latitude = status.latitude;
					gw.longitude = status.longitude;
					gw.time = status.time;
					using (LgwDbContext ctx = new LgwDbContext())
					{
						gw_db_list.Add(gw.eui, gw);
						ctx.Gateway.Add(gw);
						ctx.SaveChanges();
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(string.Format("Error: {0}", ex.Message));
					throw ex;
				}
			}
		}
		#endregion
	}

}
