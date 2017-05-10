using Microsoft.Win32;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;

namespace CloudOps_Assistant
{
    class InforTasks
    {
        public static byte[] AdditionalEntropy = { 1, 3, 4, 7, 8 };

        public static void SecureCreds(string username, string apiToken)
        {
            byte[] utf8Creds = UTF8Encoding.UTF8.GetBytes(username + ":" + apiToken);

            byte[] securedCreds = null;

            // Encrypt credentials
            try
            {
                securedCreds = ProtectedData.Protect(utf8Creds, AdditionalEntropy, DataProtectionScope.CurrentUser);

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
                byte[] securedCreds = null;
                byte[] utf8Creds = null;

                // Get encrypted key from registry
                try
                {
                    RegistryKey jenkinsCredsKey = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\Infor Logins", false);
                    securedCreds = (byte[])jenkinsCredsKey.GetValue("Infor Login");

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

            // Adding accept header for XML format
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

            // Getting the encrypted authentication details
            byte[] creds = UnsecureCreds();

            // If no authentication details, return blank message with Unauthorized status code
            if (creds == null)
            {
                HttpResponseMessage blankResponse = new HttpResponseMessage();
                blankResponse.StatusCode = System.Net.HttpStatusCode.Unauthorized;

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
                    blankResponse.StatusCode = System.Net.HttpStatusCode.Unauthorized;

                    return blankResponse;
                }

                return response;
            }
        }

        public static async Task<XmlDocument> RunInforGet(string baseUrl, string request)
        {
            // Post a GET request to Infor and wait for a response
            HttpResponseMessage getRequest = await InforGetRequest(baseUrl, request);

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