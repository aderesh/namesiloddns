using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Xml;
using System.Net;

namespace namesilo
{
    class Program
    {
        private static string IPv4Resolver = "https://api.ipify.org";
        private static string IPv6Resolver = "https://api64.ipify.org";

        static IPAddress GetMyIP(string resolverURL, AddressFamily expectedAddressFamily)
        {
            var client = new HttpClient();
            var response = client.GetAsync(resolverURL).Result;

            IPAddress ip;
            try
            {
                var ipString = response.Content.ReadAsStringAsync().Result;
                ip = IPAddress.Parse(ipString);
                Console.WriteLine($"External IP({resolverURL}): {ipString}");
            }
            catch
            {
                return null;
            }

            if (ip.AddressFamily != expectedAddressFamily)
            {
                Console.WriteLine($"Resolver '{resolverURL}' returned invalid address family for {ip}. Expected {expectedAddressFamily}. Actual {ip.AddressFamily}");
                return null;
            }

            return ip;
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
            const string ipv4ResolverVariableName = "NAMESILO_IPV4_RESOLVER";
            const string ipv6ResolverVariableName = "NAMESILO_IPV6_RESOLVER";

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
            var ipv4Resolver = Environment.GetEnvironmentVariable(ipv4ResolverVariableName) ?? IPv4Resolver;
            var ipv6Resolver = Environment.GetEnvironmentVariable(ipv6ResolverVariableName) ?? IPv6Resolver;

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
                    var ipv4 = (ipv4Resolver.ToLower() != "n/a") ? 
                        GetMyIP(ipv4Resolver, AddressFamily.InterNetwork) : null;
                    var ipv6 = (ipv6Resolver.ToLower() != "n/a") ? 
                        GetMyIP(ipv6Resolver, AddressFamily.InterNetworkV6) : null;

                    if (ipv4 == null && ipv6 == null) {
                        Console.Error.WriteLine("Cannot get current IP address");
                        return 1;
                    }

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

                    Func<Record, string> recordChecker =
                        (record) =>
                        {
                            switch(record.Type)
                            {
                                case "A":
                                        return ipv4 != null ? null : "Cannot update A record because external IPv4 was not resolved";
                                case "AAAA":
                                        return ipv6 != null ? null : "Cannot update AAAA record because external IPv6 was not resolved";
                                default:
                                    return "Only A and AAAA records can be updated";
                            }
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

                    var filtered = enrichedRecords.Where(item => !item.ReasonsToSkip.Any())
                        .Select(item => item.Record)
                        .ToList();

                    Console.WriteLine($"Updating {filtered.Count} records");
                    foreach (var record in filtered) {
                        var expectedIp = record.Type == "A" ? ipv4.ToString() : ipv6.ToString();

                        var shouldUpdate = expectedIp != record.IP;
                        var status =  !shouldUpdate ? "( )" : "(x)";
                        Console.WriteLine($"{status} {record}");

                        if (shouldUpdate)
                        {
                            Console.WriteLine($"     - IPs mismatch, updating");
                            if (!dryRun)
                            {
                                if (SetCurrentIP(record, expectedIp, apiKey))
                                {
                                    Console.WriteLine($"         ... Updated successfully");
                                }
                                else
                                {
                                    Console.WriteLine($"         ... Failed to update");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"     - IPs match, skipping");
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
