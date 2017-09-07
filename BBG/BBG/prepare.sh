sudo su

partprobe
resize2fs /dev/mmcblk0p1
df -h /

export http_proxy=http://192.168.7.1:8888
apt-get update
apt-get -y upgrade
apt-get -y install gdb mc

apt-get -y remove apache2 apache2-bin apache2-data apache2-utils
apt-get -y remove bb-wl18xx-firmware doc-beaglebone-getting-started
apt-get -y remove doc-beaglebonegreen-getting-started doc-seeed-bbgw-getting-started
apt-get -y remove nodejs roboticscape
apt-get -y remove vim vim-common vim-runtime vim-tiny
apt-get autoremove
apt-get -y install busybox-syslogd
dpkg --purge rsyslog

#insserv -r alsa-utils
apt-get -y remove --purge alsa-utils
apt-get -y remove --purge avahi-daemon

apt-get install sysv-rc-conf
apt-get clean

apt-get install openvpn
apt-get install easy-rsa
mkdir /etc/openvpn/easy-rsa/
cp -r /usr/share/easy-rsa/* /etc/openvpn/easy-rsa/
cd /etc/openvpn/easy-rsa/
