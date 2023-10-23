using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Configuration;
using FLEX_0S9_Net;

namespace FLEXNetSharp
{
    static class Program
    {
        static List<Ports> listPorts = new List<Ports>();

        static void ParseConfigFile(string[] args)
        {
            string applicationPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configPath;

            if (args.Length > 0)
            {
                configPath = args[0];
            }
            else
            {
                configPath = Path.Combine(applicationPath, "fnconfig.json");
            }

            var builder = new ConfigurationBuilder();
            builder.AddJsonFile(configPath);
            var config = builder.Build();
            var portSection = config.GetSection("Ports");
            var portParams = portSection.Get<PortParameters[]>();

            foreach (var param in portParams)
            {
                var newPort = new Ports(param);
                newPort.DisplayConfiguration();

                listPorts.Add(newPort);
            }
        }

        static void StartConnections()
        {
            foreach (Ports serialPort in listPorts)
            {
                serialPort.Open();
                var thread = new Thread(() => { serialPort.ProcessRequests(); });
                thread.Start();
            }
        }

        static void Main(string[] args)
        {
            ParseConfigFile(args);
            StartConnections();
        }
    }
}
