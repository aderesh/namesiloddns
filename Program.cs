using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Xml;
using System.Net;

namespace namesilo
{
    class Program
    {
        private static string IPResolver = "https://myexternalip.com/raw";

        static string GetMyApi()
        {
            var client = new HttpClient();
            var response = client.GetAsync(IPResolver).Result;
            return response.Content.ReadAsStringAsync().Result;
        }

        public class Record
        {
            public string Id { get; set; }
            public string IP { get; set; }
            public string Host { get; set; }
            public string Domain { get; set; }
            public string TTL { get; set; }
            public string Type { get; set; }

            public override string ToString()
            {
                return $"{Host}({Type})={IP}, TTL: {TTL}, ID: {Id}";
            }
        }

        static List<Record> GetCurrentRecords(string domain, string apiKey)
        {
            var client = new HttpClient();
            var response = client.GetAsync($"https://www.namesilo.com/api/dnsListRecords?version=1&type=xml&key={apiKey}&domain={domain}").Result;
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
                throw new Exception("Failed to retrieve value. Check API key." + status.ToString());
            }

            var records = reply.SelectNodes($"/namesilo/reply/resource_record/host");
            if (records == null) {
                return new List<Record>();
            }

            var result = new List<Record>();
            foreach(var record in records.Cast<XmlNode>()) {
                var currentIP = record.ParentNode.SelectSingleNode("value/text()").Value;
                var id = record.ParentNode.SelectSingleNode("record_id/text()").Value;
                var host = record.ParentNode.SelectSingleNode("host/text()").Value;
                var ttl = record.ParentNode.SelectSingleNode("ttl/text()").Value;
                var type = record.ParentNode.SelectSingleNode("type/text()").Value;
                result.Add(new Record { Id = id, IP = currentIP, Host = host, Domain = domain, TTL = ttl, Type = type });
            }

            return result;
        }

        public static bool SetCurrentIP(Record record, string ip, string apiKey)
        {
            var client = new HttpClient();
            var host = record.Host.Substring(0, record.Host.Length - record.Domain.Length -1);
            var request = $"https://www.namesilo.com/api/dnsUpdateRecord?version=1&type=xml&key={apiKey}&domain={record.Domain}&rrid={record.Id}&rrhost={host}&rrvalue={ip}&rrttl={record.TTL}";
            //Console.WriteLine(request);
            var response = client.GetAsync(request).Result;
            var content = response.Content.ReadAsStringAsync().Result;

            var reply = new XmlDocument();
            reply.LoadXml(content);
            var status = reply.SelectSingleNode("/namesilo/reply/code/text()");
            if (status == null)
            {
                Console.Error.WriteLine($"Failed to update record: '{record.Id}' with IP: '{ip}'.");
                return false;
            }

            if (status.Value != "300")
            {
                Console.Error.WriteLine($"Failed to update record: '{record.Id}' with IP: '{ip}'.");
                return false;
            }

            return true;
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

        static int Main(string[] args)
        {
            bool dryRun = args.Contains("--dry-run");

            Console.WriteLine("Starting" + ((dryRun) ? " [DRY RUN]" : ""));

            const string domainVariableName = "NAMESILO_DOMAIN";
            const string hostVariableName = "NAMESILO_HOST";
            const string hostRegexVariableName = "NAMESILO_HOST_REGEX";
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

            var domain = (Environment.GetEnvironmentVariable(domainVariableName) ?? "").Trim();
            var host = (Environment.GetEnvironmentVariable(hostVariableName) ?? "").Trim();
            var hostRegex = (Environment.GetEnvironmentVariable(hostRegexVariableName) ?? "").Trim();
            var apiKey = (Environment.GetEnvironmentVariable(apiKeyVariableName) ?? "").Trim();

            var delayString = Environment.GetEnvironmentVariable(delayKeyVariableName, EnvironmentVariableTarget.Process);
            var delay = TimeSpan.FromMinutes(5);

            if (!string.IsNullOrWhiteSpace(hostRegex) && (string.IsNullOrWhiteSpace(domain))) {
                throw new Exception($"{hostRegexVariableName} cannot be set with either {domainVariableName} or {hostVariableName}.");
            }

            if (string.IsNullOrEmpty(domain))
            {
                Console.Error.WriteLine($"'{domainVariableName}' is not set");
                return -1;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Console.Error.WriteLine($"'{apiKeyVariableName}' is not set");
                return -2;
            }

            bool isOneTimeExecution = false;
            if (!string.IsNullOrWhiteSpace(delayString))
            {
                delay = TimeSpan.Parse(delayString);
                isOneTimeExecution = delay == TimeSpan.Zero;

                if (isOneTimeExecution) {
                    Console.WriteLine("One-time execution. This should be run as a cron job");
                }
            }

            Console.WriteLine($"{domainVariableName}: {domain}");
            Console.WriteLine($"{hostVariableName}: {host}");
            Console.WriteLine($"{hostRegexVariableName}: {hostRegex}");
            Console.WriteLine($"{apiKeyVariableName}: {apiKey.Substring(0, 4)}...");
            Console.WriteLine($"{delayKeyVariableName}: {delay}");

            while (true)
            {
                try
                {
                    var expectedIp = GetMyApi();

                    IPAddress ip = null;

                    try
                    {
                        ip = IPAddress.Parse(expectedIp);
                    }
                    catch
                    {
                        Console.Error.WriteLine($"Invalid IP address returned from '{IPResolver}'");
                        return 1;
                    }

                    Console.WriteLine("IP: " + ip);

                    var records = GetCurrentRecords(domain, apiKey);

                    Func<Record, string> hostChecker = string.IsNullOrWhiteSpace(hostRegex) ?
                        (record) =>
                        {
                            var searchHost = (string.IsNullOrWhiteSpace(host) ? domain : $"{host}.{domain}");

                            return record.Host == searchHost ? null : $"Doesn't match '{searchHost}'. Host: '{host}', Domain: '{domain}'";
                        }
                    :
                        (record) =>
                        {
                            var regex = new Regex(hostRegex);
                            return regex.IsMatch(record.Host) ? null : $"Doesn't match regex '{hostRegex}'";
                        };

                    Func<Record, string> recordChecker = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ?
                        (record) =>
                        {
                            return record.Type == "A" ? null : "Only A record will be updated since current IP is V4";
                        }
                    :
                        (record) =>
                        {
                            return record.Type == "AAAA" ? null : "Only AAAA record will be updated since current IP is V6";
                        };

                    List<Func<Record, string>> checkers = new List<Func<Record, string>>() {
                        recordChecker,
                        hostChecker
                    };

                    var enrichedRecords = records.Select(record => new { Record = record, ReasonsToSkip = checkers.Select(ch => ch(record)).Where(r => r != null) }).ToList();

                    Console.WriteLine($"Records({enrichedRecords.Count}):");
                    foreach (var item in enrichedRecords)
                    {
                        var status = item.ReasonsToSkip.Any() ? "( )" : "(x)";
                        Console.WriteLine($"{status} {item.Record}");
                        foreach (var reason in item.ReasonsToSkip)
                        {
                            Console.WriteLine($"     - {reason}");
                        }
                    }
                    Console.WriteLine(" ");

                    var filtered = enrichedRecords.Where(item => !item.ReasonsToSkip.Any()).Select(item => item.Record);

                    foreach (var record in filtered) {
                        Console.Write(record.Host + '\t');
                        Console.Write("Current IP: " + record.IP + "\t");

                        if (expectedIp == record.IP)
                        {
                            Console.WriteLine("IPs match, skipping");
                        }
                        else
                        {
                            Console.WriteLine("IPs mismatch, updating");
                            if (!dryRun) {
                                if (SetCurrentIP(record, expectedIp, apiKey))
                                {
                                    Console.WriteLine("Updated successfully");
                                }
                                else
                                {
                                    Console.WriteLine("Failed to update");
                                }
                            }
                        }
                    }

                    Console.WriteLine($"Waiting for {delay}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }

                if (isOneTimeExecution) {
                    return 0;
                }

                Thread.Sleep(delay);
            }
        }
    }
}
