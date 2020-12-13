using System;
using System.Collections;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Xml;

namespace namesilo
{
    class Program
    {
        static string DOMAIN;
        static string APIKEY;
        static string HOST;

        static string GetMyApi()
        {
            var client = new HttpClient();
            var response = client.GetAsync("https://myexternalip.com/raw").Result;
            return response.Content.ReadAsStringAsync().Result;
        }

        public class Record
        {
            public string Id { get; set; }
            public string IP { get; set; }
        }

        static Record GetCurrentRecord()
        {
            var client = new HttpClient();
            var response = client.GetAsync($"https://www.namesilo.com/api/dnsListRecords?version=1&type=xml&key={APIKEY}&domain={DOMAIN}").Result;
            var content = response.Content.ReadAsStringAsync().Result;

            var reply = new XmlDocument();
            reply.LoadXml(content);
            var status = reply.SelectSingleNode("/namesilo/reply/code/text()");
            if (status == null)
            {
                return null;
            }

            if (status.Value != "300")
            {
                return null;
            }

            var key = string.IsNullOrWhiteSpace(HOST) ? DOMAIN : $"{HOST}.{DOMAIN}";
            var record = reply.SelectSingleNode($"/namesilo/reply/resource_record/host[text()='{key}']");

            if (record == null)
            {
                return null;
            }

            var currentIP = record.ParentNode.SelectSingleNode("value/text()").Value;
            var id = record.ParentNode.SelectSingleNode("record_id/text()").Value;

            return new Record
            {
                Id = id,
                IP = currentIP
            };
        }

        public static void SetCurrentIP(string recordId, string ip)
        {
            var client = new HttpClient();
            var response = client.GetAsync($"https://www.namesilo.com/api/dnsUpdateRecord?version=1&type=xml&key={APIKEY}&domain={DOMAIN}&rrid={recordId}&rrhost={HOST}&rrvalue={ip}&rrttl=3600").Result;
            var content = response.Content.ReadAsStringAsync().Result;

            var reply = new XmlDocument();
            reply.LoadXml(content);
            var status = reply.SelectSingleNode("/namesilo/reply/code/text()");
            if (status == null)
            {
                Console.Error.WriteLine($"Failed to update record: '{recordId}' with IP: '{ip}'.");
                return;
            }

            if (status.Value != "300")
            {
                Console.Error.WriteLine($"Failed to update record: '{recordId}' with IP: '{ip}'.");
                return;
            }

            Console.WriteLine("Updated successfully");
        }

        static void PrintEnvVariables(TextWriter writer)
        {
            writer.WriteLine("ENVIRONMENT VARIABLES: ");
            var vairables = Environment.GetEnvironmentVariables();
            foreach (var item in vairables)
            {
                var entry = (DictionaryEntry)item;
                writer.WriteLine($"{entry.Key}={entry.Value}");
            }
            writer.WriteLine();
        }

        static int Main()
        {
            Console.WriteLine("Starting");

            const string domainVariableName = "NAMESILO_DOMAIN";
            const string hostVariableName = "NAMESILO_HOST";
            const string apiKeyVariableName = "NAMESILO_APIKEY";
            const string delayKeyVariableName = "NAMESILO_DELAY";

            // DEBUGGING
            /*
            Environment.SetEnvironmentVariable(domainVariableName,  "example.org");
            Environment.SetEnvironmentVariable(hostVariableName,  "subdomain");
            Environment.SetEnvironmentVariable(apiKeyVariableName,  "<your api key>");
            */
            // END

            // PrintEnvVariables(Console.Out);

            DOMAIN = Environment.GetEnvironmentVariable(domainVariableName);
            HOST = Environment.GetEnvironmentVariable(hostVariableName);
            APIKEY = Environment.GetEnvironmentVariable(apiKeyVariableName);

            var delayString = Environment.GetEnvironmentVariable(delayKeyVariableName, EnvironmentVariableTarget.Process);
            var delay = TimeSpan.FromMinutes(5);

            if (string.IsNullOrEmpty(DOMAIN))
            {
                Console.Error.WriteLine($"'{domainVariableName}' is not set");
                return -1;
            }

            if (string.IsNullOrEmpty(APIKEY))
            {
                Console.Error.WriteLine($"'{APIKEY}' is not set");
                return -1;
            }

            if (!string.IsNullOrWhiteSpace(delayString))
            {
                delay = TimeSpan.Parse(delayString);
            }

            Console.WriteLine($"{domainVariableName}: {DOMAIN}");
            Console.WriteLine($"{hostVariableName}: {HOST}");
            Console.WriteLine($"{apiKeyVariableName}: {APIKEY}");
            Console.WriteLine($"{delayKeyVariableName}: {delay}");

            while (true)
            {
                try
                {
                    var expectedIp = GetMyApi();
                    Console.WriteLine("IP: " + expectedIp);

                    var record = GetCurrentRecord();
                    Console.WriteLine("Current IP: " + record.IP);

                    if (expectedIp == record.IP)
                    {
                        Console.WriteLine("IPs match, skipping");
                    }
                    else
                    {
                        Console.WriteLine("IPs mismatch, updating");
                        SetCurrentIP(record.Id, expectedIp);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }

                Thread.Sleep(delay);
            }
        }
    }
}
