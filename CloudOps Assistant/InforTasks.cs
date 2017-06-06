using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

namespace CloudOps_Assistant
{
    class InforTasks
    {
        private static byte[] AdditionalEntropy = { 1, 3, 4, 7, 8 };

        private static XmlSchema TicketSchema = new XmlSchema();

        private static Dictionary<string, string> TicketStatusDict = new Dictionary<string, string>();

        public static async Task<bool> GetTicketSchema(string BaseUrl)
        {
            HttpResponseMessage GetRequest = await InforGetRequest(BaseUrl, @"$schema#ticket");
            TicketSchema = XmlSchema.Read(await GetRequest.Content.ReadAsStreamAsync(), null);

            if (GetRequest.IsSuccessStatusCode)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static async Task<bool> GetTicketStatuses(string BaseUrl)
        {
            // Get statuses from Infor
            XmlDocument TicketStatusXml = await RunInforGet(BaseUrl, @"pickLists('kSYST0000337')/items");

            // Build a Namespace Manager using the namespaces from the XML
            XmlNamespaceManager TicketStatusNamespaceManager = new XmlNamespaceManager(TicketStatusXml.NameTable);
            XPathNavigator RootNode = TicketStatusXml.CreateNavigator();
            RootNode.MoveToFollowing(XPathNodeType.Element);
            IDictionary<string, string> Namespaces = RootNode.GetNamespacesInScope(XmlNamespaceScope.All);
            foreach (KeyValuePair<string, string> kvp in Namespaces)
            {
                TicketStatusNamespaceManager.AddNamespace(kvp.Key, kvp.Value);
            }

            // Get statuses from the XML
            XmlNodeList TicketStatusNodeList = TicketStatusXml.SelectNodes("//sdata:payload/slx:pickListItem", TicketStatusNamespaceManager);
            foreach (XmlNode StatusNode in TicketStatusNodeList)
            {
                string Key = StatusNode.Attributes["sdata:key"].Value;
                string Value = StatusNode.SelectSingleNode("slx:text", TicketStatusNamespaceManager).InnerText;

                TicketStatusDict.Add(Key, Value);
            }

            return true;
        }

        public static async Task<List<Ticket>> GetTicketInformation(string InforUrl)
        {
            // Get tickets from Infor
            XmlDocument TicketXml = await RunInforGet(InforUrl, @"tickets?include=assignedto,EscalationCloudOppsStaff,Urgency,TicketHistory&where=(AssignedTo.OwnerDescription eq 'APC Operations' AND StatusCode ne 'k6UJ9A000038' AND StatusCode ne 'k6UJ9A000037' AND StatusCode ne 'kCRMAA00004S' AND StatusCode ne 'kCRMAA0000GV' AND StatusCode ne 'kCRMAA0000GR' AND StatusCode ne 'kCRMAA00005U' AND StatusCode ne 'kCRMAA0000GS' AND StatusCode ne 'kCRMAA0001DN' AND StatusCode ne 'kCRMAA0001DO' AND StatusCode ne 'kCRMAA0000GX')");
            //XmlDocument TicketXml = await RunInforGet(InforUrl, @"tickets?include=assignedto,EscalationCloudOppsStaff,Urgency,TicketHistory&where=(AssignedTo.OwnerDescription eq 'APC Operations')");
            // Build a Namespace Manager using the namespaces from the XML
            XmlNamespaceManager TicketNamespaceManager = new XmlNamespaceManager(TicketXml.NameTable);
            XPathNavigator RootNode = TicketXml.CreateNavigator();
            RootNode.MoveToFollowing(XPathNodeType.Element);
            IDictionary<string, string> Namespaces = RootNode.GetNamespacesInScope(XmlNamespaceScope.All);
            foreach (KeyValuePair<string, string> kvp in Namespaces)
            {
                TicketNamespaceManager.AddNamespace(kvp.Key, kvp.Value);
            }

            // Get tickets from the XML
            XmlNodeList TicketNodeList = TicketXml.SelectNodes("//sdata:payload/slx:Ticket", TicketNamespaceManager);
            List<Ticket> TicketsOutput = new List<Ticket>();
            foreach (XmlNode TicketNode in TicketNodeList)
            {
                Ticket ticket = new Ticket();
                ticket.CreateDate = Convert.ToDateTime(TicketNode.SelectSingleNode("slx:CreateDate", TicketNamespaceManager).InnerText).ToUniversalTime();
                ticket.AssignedDate = Convert.ToDateTime(TicketNode.SelectSingleNode("slx:AssignedDate", TicketNamespaceManager).InnerText).ToUniversalTime();
                ticket.AssignedDateCorrected = ticket.AssignedDate;
                ticket.StatusCode = TicketNode.SelectSingleNode("slx:StatusCode", TicketNamespaceManager).InnerText;
                ticket.Subject = TicketNode.SelectSingleNode("slx:Subject", TicketNamespaceManager).InnerText;
                ticket.TicketNumber = TicketNode.SelectSingleNode("slx:TicketNumber", TicketNamespaceManager).InnerText;
                ticket.Urgency = TicketNode.SelectSingleNode("slx:Urgency/slx:Description", TicketNamespaceManager).InnerText;

                // Get histories for the ticket
                XmlNodeList HistoryNodeList = TicketNode.SelectNodes("slx:TicketHistory/slx:TicketHistory", TicketNamespaceManager);
                foreach (XmlNode HistoryNode in HistoryNodeList)
                {
                    TicketHistory ticketHistory = new TicketHistory();
                    ticketHistory.CreateDate = Convert.ToDateTime(HistoryNode.SelectSingleNode("slx:CreateDate", TicketNamespaceManager).InnerText).ToUniversalTime();
                    ticketHistory.FieldName = HistoryNode.SelectSingleNode("slx:FieldName", TicketNamespaceManager).InnerText;
                    ticketHistory.OldValue = HistoryNode.SelectSingleNode("slx:OldValue", TicketNamespaceManager).InnerText;
                    ticketHistory.NewValue = HistoryNode.SelectSingleNode("slx:NewValue", TicketNamespaceManager).InnerText;

                    // Check if this history is a StatusCode change
                    if (ticketHistory.FieldName == "StatusCode")
                    {
                        // Check if the status changed out of a WIP or Closed state
                        if (ticketHistory.OldValue == "k6UJ9A000038"
                            || ticketHistory.OldValue == "k6UJ9A000037"
                            || ticketHistory.OldValue == "kCRMAA00004S"
                            || ticketHistory.OldValue == "kCRMAA0000GV"
                            || ticketHistory.OldValue == "kCRMAA0000GR"
                            || ticketHistory.OldValue == "kCRMAA00005U"
                            || ticketHistory.OldValue == "kCRMAA0000GS"
                            || ticketHistory.OldValue == "kCRMAA0001DN"
                            || ticketHistory.OldValue == "kCRMAA0001DO"
                            || ticketHistory.OldValue == "kCRMAA0000GX")
                        {
                            // Check that the new status is not also a WIP or Closed state
                            if (ticketHistory.NewValue != "k6UJ9A000038"
                                && ticketHistory.NewValue != "k6UJ9A000037"
                                && ticketHistory.NewValue != "kCRMAA00004S"
                                && ticketHistory.NewValue != "kCRMAA0000GV"
                                && ticketHistory.NewValue != "kCRMAA0000GR"
                                && ticketHistory.NewValue != "kCRMAA00005U"
                                && ticketHistory.NewValue != "kCRMAA0000GS"
                                && ticketHistory.NewValue != "kCRMAA0001DN"
                                && ticketHistory.NewValue != "kCRMAA0001DO"
                                && ticketHistory.NewValue != "kCRMAA0000GX")
                            {
                                // History is for the activity changing from Closed to Open.
                                // Check if the history create date is newer than the ticket assigned date
                                if (ticketHistory.CreateDate > ticket.AssignedDateCorrected)
                                {
                                    ticket.AssignedDateCorrected = ticketHistory.CreateDate;
                                }
                            }
                        }
                    }

                    ticket.TicketHistoryList.Add(ticketHistory);
                }

                // Swap the status ID with status text
                if (TicketStatusDict.ContainsKey(ticket.StatusCode))
                {
                    ticket.StatusCode = TicketStatusDict[ticket.StatusCode];
                }

                // Get the time the ticket needs to be actioned in
                ticket.RequiredTimeToAction = new TimeSpan();
                if (MainWindow.TicketRequiredActionTimeDict.ContainsKey(ticket.Urgency))
                {
                    ticket.RequiredTimeToAction = MainWindow.TicketRequiredActionTimeDict[ticket.Urgency];
                }

                // Get the due time
                ticket.DueTime = GetTicketDueTime(ticket.RequiredTimeToAction, ticket.AssignedDateCorrected, "both");

                // Get the time the ticket has been open for (in business hours)
                ticket.TimeHasBeenOpen = GetTicketBusinessHoursOpenTime(ticket.AssignedDateCorrected, "both");

                // Calculate the remaining time left to action the ticket
                ticket.RemainingTimeToAction = ticket.RequiredTimeToAction - ticket.TimeHasBeenOpen;

                TicketsOutput.Add(ticket);
            }

            return TicketsOutput;
        }

        public static DateTime GetTicketDueTime(TimeSpan actionTime, DateTime assigned, string region)
        {
            TimeSpan OpenTime = new TimeSpan(08, 00, 00);
            TimeSpan CloseTime = new TimeSpan(24, 00, 00);

            DateTime Start = assigned;

            // Adjust start time to always be inside business hours
            if (Start < (Start.Date + OpenTime))
            {
                // Assigned time is before business hours start - push start time to start of business day
                Start = Start.Date + OpenTime;
            }
            else if (Start > (Start.Date + CloseTime))
            {
                // Assigned time is after business hours end - push start time to start of NEXT business day
                // US tickets being assigned after midnight UTC count as the morning of the next UTC day, and are handled above
                Start = Start.Date + OpenTime + new TimeSpan(1, 00, 00, 00);
            }

            // Adjust to skip weekends
            if (Start.DayOfWeek == DayOfWeek.Saturday)
            {
                // Move to Monday morning
                Start = Start + new TimeSpan(2, 00, 00, 00);
            }
            else if (Start.DayOfWeek == DayOfWeek.Sunday)
            {
                // Move to Monday morning
                Start = Start + new TimeSpan(1, 00, 00, 00);
            }

            DateTime DueTime = Start + actionTime;
            DateTime DayEnd = Start.Date + CloseTime;
            
            // While due time is after business hours, bump it to the next day
            while (DueTime > DayEnd)
            {
                // Get time left over after day ends
                TimeSpan Remainder = DueTime - DayEnd;

                // Bump until the next day (plus the remaining time)
                DueTime = DueTime.Date + new TimeSpan(1, 00, 00, 00) + OpenTime + Remainder;

                // Adjust to skip weekends
                if (DueTime.DayOfWeek == DayOfWeek.Saturday)
                {
                    // Move to Monday morning
                    DueTime = DueTime.Date + new TimeSpan(2, 00, 00, 00) + OpenTime + Remainder;
                }
                else if (DueTime.DayOfWeek == DayOfWeek.Sunday)
                {
                    // Move to Monday morning
                    DueTime = DueTime.Date + new TimeSpan(1, 00, 00, 00) + OpenTime + Remainder;
                }

                // Reset day end based on the new ticket DueTime
                DayEnd = DueTime.Date + CloseTime;
            }

            return DueTime;
        }

        public static TimeSpan GetTicketBusinessHoursOpenTime(DateTime assigned, string region)
        {
            TimeSpan OpenTime = new TimeSpan(08, 00, 00);
            TimeSpan CloseTime = new TimeSpan(24, 00, 00);
            TimeSpan UsedTime = new TimeSpan();

            DateTime AssessDate = assigned; // Assess date moves forwards until today's date and is used to assess durations

            // Straight up deduct the time the ticket was opened from UsedTime, if it was opened during office hours - we'll add this back later on
            if (AssessDate.TimeOfDay > OpenTime && AssessDate.TimeOfDay < CloseTime)
            {
                UsedTime = UsedTime - (AssessDate.TimeOfDay - OpenTime);
            }

            // Adjust assess time to always be inside business hours
            if (AssessDate < (AssessDate.Date + OpenTime))
            {
                // Assigned time is before business hours start - push assess time to start of business day
                AssessDate = AssessDate.Date + OpenTime;
            }
            else if (AssessDate > (AssessDate.Date + CloseTime))
            {
                // Assess time is after business hours end - push to start of NEXT business day
                // US tickets being assigned after midnight UTC count as the morning of the next UTC day, and are handled above
                AssessDate = AssessDate.Date + new TimeSpan(1, 00, 00, 00) + OpenTime;
            }

            // Adjust to skip weekends
            if (AssessDate.DayOfWeek == DayOfWeek.Saturday)
            {
                // Move to Monday morning
                AssessDate = AssessDate + new TimeSpan(2, 00, 00, 00) + OpenTime;
            }
            else if (AssessDate.DayOfWeek == DayOfWeek.Sunday)
            {
                // Move to Monday morning
                AssessDate = AssessDate + new TimeSpan(1, 00, 00, 00) + OpenTime;
            }

            DateTime TimeNow = DateTime.UtcNow;

            // If AssessDate is before today, mega maths
            if (AssessDate.Date < TimeNow.Date)
            {
                // Loop until on the current day
                while (AssessDate.Date < TimeNow.Date)
                {
                    // Bump AssessDate to the next day (plus the remaining time)
                    AssessDate = AssessDate.Date + new TimeSpan(1, 00, 00, 00) + OpenTime;

                    // Adjust to skip weekends
                    if (AssessDate.DayOfWeek == DayOfWeek.Saturday)
                    {
                        // Move to Monday morning
                        AssessDate = AssessDate.Date + new TimeSpan(2, 00, 00, 00) + OpenTime;
                    }
                    else if (AssessDate.DayOfWeek == DayOfWeek.Sunday)
                    {
                        // Move to Monday morning
                        AssessDate = AssessDate.Date + new TimeSpan(1, 00, 00, 00) + OpenTime;
                    }

                    // Add the day's business hours to UsedTime
                    UsedTime = UsedTime + (CloseTime - OpenTime);
                }
            }

            // If current time is after the day start, maths - else keep UsedTime as-is
            if (TimeNow > (TimeNow.Date + OpenTime))
            {
                // If current time is before the day end, maths - else add today's business hours to UsedTime
                if (TimeNow < (TimeNow.Date + CloseTime))
                {
                    UsedTime = UsedTime + (TimeNow.TimeOfDay - OpenTime);
                }
                else
                {
                    UsedTime = UsedTime + (CloseTime - OpenTime);
                }
            }

            return UsedTime;
        }

        public static int GetTicketActionTimePercent(TimeSpan openTime, string urgency)
        {
            TimeSpan actionTime = new TimeSpan();

            if (MainWindow.TicketRequiredActionTimeDict.ContainsKey(urgency))
            {
                actionTime = MainWindow.TicketRequiredActionTimeDict[urgency];
            }

            return Convert.ToInt32(100 * openTime.Ticks / actionTime.Ticks);
        }

        public static void SecureCreds(string username, string password)
        {
            byte[] utf8Creds = Encoding.UTF8.GetBytes(username + ":" + password);

            // Encrypt credentials
            try
            {
                byte[] securedCreds = ProtectedData.Protect(utf8Creds, AdditionalEntropy, DataProtectionScope.CurrentUser);

                // Check if registry path exists
                if (CheckOrCreateRegPath())
                {
                    // Save encrypted key to registry
                    RegistryKey credsKey = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Infor Logins", true);
                    credsKey.SetValue("Infor Login", securedCreds);
                }
            }
            catch (CryptographicException e)
            {
                MessageBox.Show("Unable to encrypt Infor login credentials:\n\n" + e.ToString());
            }
        }

        public static byte[] UnsecureCreds()
        {
            // Check if registry path exists
            if (CheckOrCreateRegPath())
            {
                byte[] utf8Creds = null;

                // Get encrypted key from registry
                try
                {
                    RegistryKey Key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Infor Logins", false);
                    byte[] securedCreds = (byte[])Key.GetValue("Infor Login");

                    // Un-encrypt credentials
                    try
                    {
                        utf8Creds = ProtectedData.Unprotect(securedCreds, AdditionalEntropy, DataProtectionScope.CurrentUser);
                    }
                    catch (CryptographicException e)
                    {
                        MessageBox.Show("Unable to unencrypt Infor login credentials:\n\n" + e.ToString());
                    }
                }
                catch (Exception error)
                {
                    MessageBox.Show("Unable to get stored Infor credentials\n\n" + error.Message);
                }

                return utf8Creds;
            }
            return null;
        }

        public static bool CheckOrCreateRegPath()
        {
            // Check if SubKey HKCU\Software\Swiftpage Support\JenkinsLogins exists
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Infor Logins", false);
            if (key == null)
            {
                // Doesn't exist, let's see if HKCU\Software\Swiftpage Support exists
                key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support", false);
                if (key == null)
                {
                    // Doesn't exist, try to create 'Swiftpage Support' SubKey
                    key = Registry.CurrentUser.OpenSubKey(@"Software", true);
                    try
                    {
                        key.CreateSubKey("Swiftpage Support");
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(@"Unable to create SubKey HKCU\Software\Swiftpage Support:\n\n" + error.Message);
                        return false;
                    }
                }

                // 'Swiftpage Support' subkey exists (or has just been created), try creating 'Infor Logins'
                key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support", true);
                try
                {
                    key.CreateSubKey("Infor Logins");
                }
                catch (Exception error)
                {
                    MessageBox.Show(@"Unable to create SubKey HKCU\Software\Swiftpage Support\Infor Logins:\n\n" + error.Message);
                    return false;
                }
            }
            return true;
        }

        public static async Task<HttpResponseMessage> InforGetRequest(string baseUrl, string request)
        {
            // Create HttpClient with base URL
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(baseUrl);
            
            // Getting the encrypted authentication details
            byte[] creds = UnsecureCreds();
            
            // If no authentication details, return blank message with Unauthorized status code
            if (creds == null)
            {
                HttpResponseMessage blankResponse = new HttpResponseMessage();
                blankResponse.StatusCode = HttpStatusCode.Unauthorized;

                return blankResponse;
            }
            else
            {

                // Add authentication details to HTTP request
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(creds));
                
                // Run a Get request with the provided request path
                HttpResponseMessage response = new HttpResponseMessage();
                try
                {
                    response = await client.GetAsync(request);
                }
                catch (Exception error)
                {
                    MessageBox.Show("GET request failed in 'inforGetRequest(" + baseUrl + request + ")'.\n\n" + error);

                    HttpResponseMessage blankResponse = new HttpResponseMessage();
                    blankResponse.StatusCode = HttpStatusCode.Unauthorized;

                    MessageBox.Show(error.Message);

                    return blankResponse;
                }
                
                return response;
            }
        }

        public static async Task<XmlDocument> RunInforGet(string baseUrl, string request)
        {
            // Post a GET request to Infor and wait for a response
            HttpResponseMessage getRequest = await InforGetRequest(baseUrl, request);

            if (!getRequest.IsSuccessStatusCode)
            {
                MessageBox.Show(getRequest.StatusCode + " " + getRequest.ReasonPhrase + " " + getRequest.RequestMessage);
            }

            XmlDocument xmlOutput = new XmlDocument();
            xmlOutput.LoadXml(await getRequest.Content.ReadAsStringAsync());

            return xmlOutput;
        }

        public static async Task<HttpResponseMessage> NoAuthGetRequest(string baseUrl, string request)
        {
            // Create HttpClient with base URL
            HttpClient client = new HttpClient()
            {
                BaseAddress = new Uri(baseUrl)
            };

            // Adding accept header for XML format
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

            // Run a Get request with the provided request path
            HttpResponseMessage response = new HttpResponseMessage();
            try
            {
                ServicePointManager.CertificatePolicy = new MyPolicy();
                response = await client.GetAsync(request);
            }
            catch (Exception error)
            {
                MessageBox.Show("GET request failed in 'NoAuthGetRequest(" + baseUrl + request + ")'.\n\n" + error);

                HttpResponseMessage blankResponse = new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.Unauthorized
                };
                return blankResponse;
            }

            return response;
        }

        public static async Task<XmlDocument> RunNoAuthGet(string baseUrl, string request)
        {
            // Post a GET request to Infor and wait for a response
            HttpResponseMessage getRequest = await NoAuthGetRequest(baseUrl, request);

            XmlDocument xmlOutput = new XmlDocument();
            xmlOutput.LoadXml(await getRequest.Content.ReadAsStringAsync());

            return xmlOutput;
        }
    }

    public class Ticket
    {
        public DateTime CreateDate { get; set; }
        public DateTime AssignedDate { get; set; }
        public DateTime AssignedDateCorrected { get; set; }
        public string StatusCode { get; set; }
        public string Subject { get; set; }
        public string TicketNumber { get; set; }
        public List<TicketHistory> TicketHistoryList = new List<TicketHistory>();
        public string Urgency { get; set; }
        public DateTime DueTime { get; set; }
        public TimeSpan TimeHasBeenOpen { get; set; }
        public TimeSpan RequiredTimeToAction { get; set; }
        public TimeSpan RemainingTimeToAction { get; set; }
    }

    public class TicketHistory
    {
        public DateTime CreateDate { get; set; }
        public string FieldName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public class MyPolicy : ICertificatePolicy
    {
        public bool CheckValidationResult(ServicePoint srvPoint, X509Certificate certificate, WebRequest request,
      int certificateProblem)
        {
            //Return True to force the certificate to be accepted.
            return true;
        }
    }
}