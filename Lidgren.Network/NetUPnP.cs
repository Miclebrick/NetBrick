﻿using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;
#if !__NOIPENDPOINT__
using NetEndPoint = System.Net.IPEndPoint;

#endif

namespace Lidgren.Network
{
    /// <summary>
    ///     Status of the UPnP capabilities
    /// </summary>
    public enum UPnPStatus
    {
        /// <summary>
        ///     Still discovering UPnP capabilities
        /// </summary>
        Discovering,

        /// <summary>
        ///     UPnP is not available
        /// </summary>
        NotAvailable,

        /// <summary>
        ///     UPnP is available and ready to use
        /// </summary>
        Available
    }

    /// <summary>
    ///     UPnP support class
    /// </summary>
    public class NetUPnP
    {
        private const int c_discoveryTimeOutMillis = 1000;
        private readonly ManualResetEvent m_discoveryComplete = new ManualResetEvent(false);
        private readonly NetPeer m_peer;
        internal double m_discoveryResponseDeadline;
        private string m_serviceName = "";
        private string m_serviceUrl;

        /// <summary>
        ///     NetUPnP constructor
        /// </summary>
        public NetUPnP(NetPeer peer)
        {
            m_peer = peer;
            m_discoveryResponseDeadline = double.MinValue;
        }

        /// <summary>
        ///     Status of the UPnP capabilities of this NetPeer
        /// </summary>
        public UPnPStatus Status { get; private set; }

        internal void Discover(NetPeer peer)
        {
            var str =
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "ST:upnp:rootdevice\r\n" +
                "MAN:\"ssdp:discover\"\r\n" +
                "MX:3\r\n\r\n";

            Status = UPnPStatus.Discovering;

            var arr = Encoding.UTF8.GetBytes(str);

            m_peer.LogDebug("Attempting UPnP discovery");
            peer.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            peer.RawSend(arr, 0, arr.Length, new NetEndPoint(NetUtility.GetBroadcastAddress(), 1900));
            peer.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, false);

            // allow some extra time for router to respond
            NetUtility.Sleep(50);

            m_discoveryResponseDeadline = NetTime.Now + 6.0;
                // arbitrarily chosen number, router gets 6 seconds to respond
            Status = UPnPStatus.Discovering;
        }

        internal void ExtractServiceUrl(string resp)
        {
#if !DEBUG
			try
			{
#endif
            var desc = new XmlDocument();
            desc.Load(WebRequest.Create(resp).GetResponse().GetResponseStream());
            var nsMgr = new XmlNamespaceManager(desc.NameTable);
            nsMgr.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");
            var typen = desc.SelectSingleNode("//tns:device/tns:deviceType/text()", nsMgr);
            if (!typen.Value.Contains("InternetGatewayDevice"))
                return;

            m_serviceName = "WANIPConnection";
            var node =
                desc.SelectSingleNode(
                    "//tns:service[tns:serviceType=\"urn:schemas-upnp-org:service:" + m_serviceName +
                    ":1\"]/tns:controlURL/text()", nsMgr);
            if (node == null)
            {
                //try another service name
                m_serviceName = "WANPPPConnection";
                node =
                    desc.SelectSingleNode(
                        "//tns:service[tns:serviceType=\"urn:schemas-upnp-org:service:" + m_serviceName +
                        ":1\"]/tns:controlURL/text()", nsMgr);
                if (node == null)
                    return;
            }

            m_serviceUrl = CombineUrls(resp, node.Value);
            m_peer.LogDebug("UPnP service ready");
            Status = UPnPStatus.Available;
            m_discoveryComplete.Set();
#if !DEBUG
			}
			catch
			{
				m_peer.LogVerbose("Exception ignored trying to parse UPnP XML response");
				return;
			}
#endif
        }

        private static string CombineUrls(string gatewayURL, string subURL)
        {
            // Is Control URL an absolute URL?
            if ((subURL.Contains("http:")) || (subURL.Contains(".")))
                return subURL;

            gatewayURL = gatewayURL.Replace("http://", ""); // strip any protocol
            var n = gatewayURL.IndexOf("/");
            if (n != -1)
                gatewayURL = gatewayURL.Substring(0, n); // Use first portion of URL
            return "http://" + gatewayURL + subURL;
        }

        private bool CheckAvailability()
        {
            switch (Status)
            {
                case UPnPStatus.NotAvailable:
                    return false;
                case UPnPStatus.Available:
                    return true;
                case UPnPStatus.Discovering:
                    if (m_discoveryComplete.WaitOne(c_discoveryTimeOutMillis))
                        return true;
                    if (NetTime.Now > m_discoveryResponseDeadline)
                        Status = UPnPStatus.NotAvailable;
                    return false;
            }
            return false;
        }

        /// <summary>
        ///     Add a forwarding rule to the router using UPnP
        /// </summary>
        public bool ForwardPort(int port, string description)
        {
            if (!CheckAvailability())
                return false;

            IPAddress mask;
            var client = NetUtility.GetMyAddress(out mask);
            if (client == null)
                return false;

            try
            {
                SOAPRequest(m_serviceUrl,
                    "<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:" + m_serviceName + ":1\">" +
                    "<NewRemoteHost></NewRemoteHost>" +
                    "<NewExternalPort>" + port + "</NewExternalPort>" +
                    "<NewProtocol>" + ProtocolType.Udp.ToString().ToUpper(CultureInfo.InvariantCulture) +
                    "</NewProtocol>" +
                    "<NewInternalPort>" + port + "</NewInternalPort>" +
                    "<NewInternalClient>" + client + "</NewInternalClient>" +
                    "<NewEnabled>1</NewEnabled>" +
                    "<NewPortMappingDescription>" + description + "</NewPortMappingDescription>" +
                    "<NewLeaseDuration>0</NewLeaseDuration>" +
                    "</u:AddPortMapping>",
                    "AddPortMapping");

                m_peer.LogDebug("Sent UPnP port forward request");
                NetUtility.Sleep(50);
            }
            catch (Exception ex)
            {
                m_peer.LogWarning("UPnP port forward failed: " + ex.Message);
                return false;
            }
            return true;
        }

        /// <summary>
        ///     Delete a forwarding rule from the router using UPnP
        /// </summary>
        public bool DeleteForwardingRule(int port)
        {
            if (!CheckAvailability())
                return false;

            try
            {
                SOAPRequest(m_serviceUrl,
                    "<u:DeletePortMapping xmlns:u=\"urn:schemas-upnp-org:service:" + m_serviceName + ":1\">" +
                    "<NewRemoteHost>" +
                    "</NewRemoteHost>" +
                    "<NewExternalPort>" + port + "</NewExternalPort>" +
                    "<NewProtocol>" + ProtocolType.Udp.ToString().ToUpper(CultureInfo.InvariantCulture) +
                    "</NewProtocol>" +
                    "</u:DeletePortMapping>", "DeletePortMapping");
                return true;
            }
            catch (Exception ex)
            {
                m_peer.LogWarning("UPnP delete forwarding rule failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        ///     Retrieve the extern ip using UPnP
        /// </summary>
        public IPAddress GetExternalIP()
        {
            if (!CheckAvailability())
                return null;
            try
            {
                var xdoc = SOAPRequest(m_serviceUrl,
                    "<u:GetExternalIPAddress xmlns:u=\"urn:schemas-upnp-org:service:" + m_serviceName + ":1\">" +
                    "</u:GetExternalIPAddress>", "GetExternalIPAddress");
                var nsMgr = new XmlNamespaceManager(xdoc.NameTable);
                nsMgr.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");
                var IP = xdoc.SelectSingleNode("//NewExternalIPAddress/text()", nsMgr).Value;
                return IPAddress.Parse(IP);
            }
            catch (Exception ex)
            {
                m_peer.LogWarning("Failed to get external IP: " + ex.Message);
                return null;
            }
        }

        private XmlDocument SOAPRequest(string url, string soap, string function)
        {
            var req = "<?xml version=\"1.0\"?>" +
                      "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                      "<s:Body>" +
                      soap +
                      "</s:Body>" +
                      "</s:Envelope>";
            var r = WebRequest.Create(url);
            r.Method = "POST";
            var b = Encoding.UTF8.GetBytes(req);
            r.Headers.Add("SOAPACTION", "\"urn:schemas-upnp-org:service:" + m_serviceName + ":1#" + function + "\"");
            r.ContentType = "text/xml; charset=\"utf-8\"";
            r.ContentLength = b.Length;
            r.GetRequestStream().Write(b, 0, b.Length);
            var resp = new XmlDocument();
            var wres = r.GetResponse();
            var ress = wres.GetResponseStream();
            resp.Load(ress);
            return resp;
        }
    }
}