using System;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

internal class AppsflyerModule
{
    private bool isSandbox { get; }
    private string devkey { get; }
    private string appid { get; }
    private int af_counter { get; set; }
    private string af_device_id { get; }
    private string cuid { get; set; }
    private bool isStopped { get; set; }
    private string app_version { get; }
    private string device_model { get; }
    private string device_os_ver { get; }

    public AppsflyerModule(string devkey, string appid, string app_version, 
        string device_model, string device_os_ver, 
        bool isSandbox = false)
    {
        this.isSandbox = isSandbox;
        this.devkey = devkey;
        this.appid = appid;
        this.app_version = app_version;
        this.device_model = device_model;
        this.device_os_ver = device_os_ver;
        this.isStopped = true;

        this.af_counter = PlayerPrefs.GetInt("af_counter");
        // Debug.Log("af_counter: " + af_counter);

        this.af_device_id = PlayerPrefs.GetString("af_device_id");

        //in case there's no AF-ID yet
        if (String.IsNullOrEmpty(af_device_id))
        {
            af_device_id = GenerateGuid();
            PlayerPrefs.SetString("af_device_id", af_device_id);
        }

        Debug.Log("af_device_id: " + af_device_id);
    }

    private RequestData CreateRequestData()
    {
        // setting the device ids and request body
        DeviceIDs deviceid = new DeviceIDs { type = "custom", value = af_device_id };
        DeviceIDs[] deviceids = { deviceid };

        RequestData req = new RequestData
        {
            timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            device_model = device_model,
            device_os_version = TrimDeviceOsVer(device_os_ver),
            app_version = app_version,
            device_ids = deviceids,
            request_id = GenerateGuid(),
            limit_ad_tracking = false,
            customer_user_id = cuid

        };
        return req;
    }

    private static string TrimDeviceOsVer(string device_os_ver)
    {
        if (device_os_ver.IndexOf(" (") > -1)
            device_os_ver = device_os_ver.Replace(" (", "");
        if (device_os_ver.IndexOf("(") > -1)
            device_os_ver = device_os_ver.Replace("(", "");
        if (device_os_ver.IndexOf(")") > -1)
            device_os_ver = device_os_ver.Replace(")", "");
        if (device_os_ver.IndexOf("%20") > -1)
            device_os_ver = device_os_ver.Replace("%20", "-");
        if (device_os_ver.IndexOf(" ") > -1)
            device_os_ver = device_os_ver.Replace(" ", "-");
        device_os_ver = Regex.Replace(device_os_ver, "[^0-9.+-]", "");
        if (device_os_ver.IndexOf("-") == 0)
            device_os_ver = device_os_ver.Substring(1, device_os_ver.Length - 1);
        if (device_os_ver.Length > 23)
            device_os_ver = device_os_ver.Substring(0, 23);
        return device_os_ver;
    }

    // report first open event to AppsFlyer (or session if counter > 2)
    public void Start(bool skipFirst = false)
    {
        this.isStopped = false;
        // generating the request data
        RequestData req = CreateRequestData();

        // set request type
        AppsflyerRequestType REQ_TYPE =
            af_counter < 2 && !skipFirst
                ? AppsflyerRequestType.FIRST_OPEN_REQUEST
                : AppsflyerRequestType.SESSION_REQUEST;

        // post the request via Unity http client
        SendUnityPostReq(req, REQ_TYPE);
    }

    public void Stop()
    {
        isStopped = true;
        Debug.LogWarning("Appsflyer SDK has been stopped.");
    }

    // report inapp event to AppsFlyer
    public void LogEvent(
        string event_name,
        Dictionary<string, object> event_parameters,
        Dictionary<string, object> event_custom_parameters = null
    )
    {
        if (isStopped)
        {
            Debug.LogWarning("Cannot send LogEvent, the Appsflyer SDK is stopped");
            return;
        }

        // generating the request data
        RequestData req = CreateRequestData();
        // setting the event name and value
        req.event_name = event_name;
        req.event_parameters = event_parameters;
        req.event_custom_parameters = event_custom_parameters;

        // set request type
        AppsflyerRequestType REQ_TYPE = AppsflyerRequestType.INAPP_EVENT_REQUEST;

        // post the request via Unity http client
        SendUnityPostReq(req, REQ_TYPE);
    }

    public bool IsInstallOlderThanDate(string date)
    {
        bool isInstallOlder = false;

        string dataPath = Application.dataPath;
        // Debug.Log("dataPath: " + dataPath);
        DateTime createdTime = Directory.GetCreationTime(dataPath);
        // Debug.Log("createdTime: " + createdTime);
        DateTime checkDate = DateTime.Parse(date);
        // Debug.Log("checkDate: " + checkDate);

        if (createdTime != null)
        {
            isInstallOlder = DateTime.Compare(createdTime, checkDate) < 0;
        }

        return isInstallOlder;
    }

    public string GetAppsFlyerUID()
    {
        return this.af_device_id;
    }

    public void SetCustomerUserId(string cuid)
    {
        if (!isStopped)
        {
            Debug.LogWarning("Cannot set CustomerUserID while the SDK has started.");
            return;
        }
        Debug.Log("Customer User ID has been set");
        this.cuid = cuid;
    }

    // send post request with Unity HTTP Client
    private async void SendUnityPostReq(RequestData req, AppsflyerRequestType REQ_TYPE)
    {
        // serialize the json and remove empty fields
        string json = JsonConvert.SerializeObject(
            req,
            Newtonsoft.Json.Formatting.None,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
        );

        // create auth token
        string auth = HmacSha256Digest(json, devkey);

        // define the url based on the request type
        string url;
        switch (REQ_TYPE)
        {
            case AppsflyerRequestType.FIRST_OPEN_REQUEST:
                url = isSandbox
                    ? "https://sandbox-events.appsflyer.com/v1.0/c2s/first_open/app/nativepc/"
                        + appid
                    : "https://events.appsflyer.com/v1.0/c2s/first_open/app/nativepc/" + appid;
                break;
            case AppsflyerRequestType.SESSION_REQUEST:
                url = isSandbox
                    ? "https://sandbox-events.appsflyer.com/v1.0/c2s/session/app/nativepc/" + appid
                    : "https://events.appsflyer.com/v1.0/c2s/session/app/nativepc/" + appid;
                break;
            case AppsflyerRequestType.INAPP_EVENT_REQUEST:
                url = isSandbox
                    ? "https://sandbox-events.appsflyer.com/v1.0/c2s/inapp/app/nativepc/" + appid
                    : "https://events.appsflyer.com/v1.0/c2s/inapp/app/nativepc/" + appid;
                break;
            default:
                url = null;
                break;
        }

        // set the request body
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

        // set the request content type
        uwr.SetRequestHeader("Content-Type", "application/json");
        // set the authorization
        uwr.SetRequestHeader("Authorization", auth);
        uwr.SetRequestHeader(
            "user-agent",
            "UnityGamesLauncher/"
                + " ("
                + SystemInfo.operatingSystem.Replace("(", "").Replace(")", "")
                + ")"
        );

        //Send the request then wait here until it returns
        var operation = uwr.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        switch (REQ_TYPE)
        {
            case AppsflyerRequestType.FIRST_OPEN_REQUEST:
                Debug.Log("Request type: FIRST_OPEN_REQUEST");
                break;
            case AppsflyerRequestType.SESSION_REQUEST:
                Debug.Log("Request type: SESSION_REQUEST");
                PlayerPrefs.SetInt("af_counter", af_counter);
                break;
            case AppsflyerRequestType.INAPP_EVENT_REQUEST:
                Debug.Log("Request type: INAPP_EVENT_REQUEST");
                break;
        }
        Debug.Log("Is success: " + uwr.result);
        Debug.Log("Response Code: " + uwr.responseCode);
        string resCode = uwr.responseCode.ToString();

        if (uwr.result == UnityWebRequest.Result.ConnectionError)
        {
            Debug.Log("Error While Sending: " + uwr.error);
            // TODO: handle/log error
        }
        else if (resCode == "202" || resCode == "200")
        {
            switch (REQ_TYPE)
            {
                // increase the appsflyer counter on a first-open/session request
                case AppsflyerRequestType.FIRST_OPEN_REQUEST:
                case AppsflyerRequestType.SESSION_REQUEST:
                    af_counter++;
                    PlayerPrefs.SetInt("af_counter", af_counter);
                    break;
                case AppsflyerRequestType.INAPP_EVENT_REQUEST:
                    break;
            }
        }
        else
        {
            Debug.Log("error: " + uwr.error);
            if (!isSandbox)
            {
                Debug.Log(
                    "Please try to send the request to 'sandbox-events.appsflyer.com' instead of 'events.appsflyer.com' in order to debug."
                );
            }
            else
            {
                Debug.Log("error detail: " + uwr.downloadHandler.text);
            }
        }

        uwr.Dispose();
    }

    // generate GUID for post request and AF id
    private string GenerateGuid()
    {
        Guid myuuid = Guid.NewGuid();
        return myuuid.ToString();
    }

    // generate hmac auth for post requests
    private string HmacSha256Digest(string message, string secret)
    {
        UTF8Encoding encoding = new UTF8Encoding();
        byte[] keyBytes = encoding.GetBytes(secret);
        byte[] messageBytes = encoding.GetBytes(message);
        System.Security.Cryptography.HMACSHA256 cryptographer =
            new System.Security.Cryptography.HMACSHA256(keyBytes);

        byte[] bytes = cryptographer.ComputeHash(messageBytes);

        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }
}

[Flags]
enum AppsflyerRequestType : ulong
{
    FIRST_OPEN_REQUEST = 100,
    SESSION_REQUEST = 101,
    INAPP_EVENT_REQUEST = 102
}

[Serializable]
class RequestData
{
    public long timestamp;
    public string device_os_version;
    public string device_model;
    public string app_version;
    public DeviceIDs[] device_ids;
    public string request_id;
    public bool limit_ad_tracking;
    public string customer_user_id;
    public string event_name;
    public Dictionary<string, object> event_parameters;
    public Dictionary<string, object> event_custom_parameters;
}

[Serializable]
class DeviceIDs
{
    public string type;
    public string value;
}