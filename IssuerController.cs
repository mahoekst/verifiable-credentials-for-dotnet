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

namespace Verifiable_credentials_DotNet
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class IssuerController : ControllerBase
    {
        const string ISSUANCEPAYLOAD = "issuance_request_config.json";
        const string APIENDPOINT = "https://dev.did.msidentity.com/v1.0/abc/verifiablecredentials/request";

        [HttpGet("/api/issuer/issue-request")]
        public async Task<ActionResult> issueRequest()
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
                    //requestConfig.Add(new JProperty("link", requestConfig["url"].ToString()));
                    jsonString = JsonConvert.SerializeObject(requestConfig);
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

                return new OkResult();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }
        }

    }
}
