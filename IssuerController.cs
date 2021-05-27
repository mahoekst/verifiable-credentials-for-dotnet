using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;

namespace Verifiable_credentials_DotNet
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class IssuerController : ControllerBase
    {
        const string ISSUANCEPAYLOAD = "issuance_request_config.json";
        const string APIENDPOINT = "https://dev.did.msidentity.com/v1.0/abc/verifiablecredentials/request";

        protected IMemoryCache _cache;

        public IssuerController(IMemoryCache memoryCache)
        {
            _cache = memoryCache;
        }

        [HttpGet("/api/issuer/issuance-request")]
        public async Task<ActionResult> issuanceRequest()
        {
            try
            {
                string jsonString = null;
                string newpin = null;

                string payloadpath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), ISSUANCEPAYLOAD);
                if (!System.IO.File.Exists(payloadpath)) { return BadRequest(new { error = "400", error_description = ISSUANCEPAYLOAD + " not found" }); }
                jsonString = System.IO.File.ReadAllText(payloadpath);
                if (string.IsNullOrEmpty(jsonString)) { return BadRequest(new { error = "400", error_description = ISSUANCEPAYLOAD + " not found" }); }

                string state = Guid.NewGuid().ToString();

                //check if pin is required, if found make sure we set a new random pin
                JObject config = JObject.Parse(jsonString);
                if (config["issuance"]["pin"] != null)
                {
                    var length = (int)config["issuance"]["pin"]["length"];
                    var pinMaxValue = (int)Math.Pow(10, length) - 1;
                    var randomNumber = RandomNumberGenerator.GetInt32(1, pinMaxValue);
                    newpin = string.Format("{0:D" + length.ToString() + "}", randomNumber);
                    config["issuance"]["pin"]["value"] = newpin;
                }

                if (config["callback"]["state"] != null)
                {
                    config["callback"]["state"] = state;
                }

                jsonString = JsonConvert.SerializeObject(config);

                //CALL REST API WITH PAYLOAD
                HttpStatusCode statusCode = HttpStatusCode.OK;
                string response = null;
                try
                {
                    HttpClient client = new HttpClient();
                    HttpResponseMessage res = client.PostAsync(APIENDPOINT, new StringContent(jsonString, Encoding.UTF8, "application/json")).Result;
                    response = res.Content.ReadAsStringAsync().Result;
                    client.Dispose();
                    statusCode = res.StatusCode;

                    JObject requestConfig = JObject.Parse(response);
                    if (newpin != null) { requestConfig["pin"] = newpin; }
                    requestConfig.Add(new JProperty("id", state));
                    jsonString = JsonConvert.SerializeObject(requestConfig);

                    var cacheData = new
                    {
                        status = "notscanned",
                        message = "Request ready, please scan with Authenticator",
                        expiry = requestConfig["expiry"].ToString()
                    };
                    _cache.Set(state, JsonConvert.SerializeObject(cacheData));

                    return new ContentResult { ContentType = "application/json", Content = jsonString };
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = "400", error_description = "Something went wrong calling the API: " + ex.Message });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }
        }


        [HttpPost("/api/issuer/issuanceCallback")]
        public async Task<ActionResult> issuanceCallback()
        {
            try
            {
                string content = new System.IO.StreamReader(this.Request.Body).ReadToEndAsync().Result;
                Debug.WriteLine("callback!: " + content);
                JObject issuanceResponse = JObject.Parse(content);
                var state = issuanceResponse["state"].ToString();

                if (issuanceResponse["code"].ToString() == "request_retrieved")
                {
                    var cacheData = new
                    {
                        status = "request_retrieved",
                        message = "QR Code is scanned. Waiting for issuance...",
                    };
                    _cache.Set(state, JsonConvert.SerializeObject(cacheData));
                }

                //
                //THIS IS NOT IMPLEMENTED IN OUR SERVICE YET, ONLY MOCKUP FOR ONCE WE DO SUPPORT THE CALLBACK AFTER ISSUANCE
                //
                if (issuanceResponse["code"].ToString() == "credential_issued")
                {
                    var cacheData = new
                    {
                        status = "credential_issued",
                        message = "Credential succesful issued",
                        payload = issuanceResponse["issuers"].ToString(),
                        subject = issuanceResponse["subject"].ToString()
                    };
                    _cache.Set(state, JsonConvert.SerializeObject(cacheData));
                }

                return new OkResult();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }
        }

        [HttpGet("/api/issuer/issuance-response")]
        public async Task<ActionResult> issuanceResponse()
        {
            try
            {
                string state = this.Request.Query["id"];
                if (string.IsNullOrEmpty(state))
                {
                    return BadRequest(new { error = "400", error_description = "Missing argument 'id'" });
                }
                JObject value = null;
                if (_cache.TryGetValue(state, out string buf))
                {
                    value = JObject.Parse(buf);

                    Debug.WriteLine("check if there was a response yet: " + value);
                    return new ContentResult { ContentType = "application/json", Content = JsonConvert.SerializeObject(value) }; 
                }

                return new OkResult();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }

        }
    }
}
