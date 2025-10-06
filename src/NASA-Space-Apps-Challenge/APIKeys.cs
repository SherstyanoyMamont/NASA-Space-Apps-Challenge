using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;



namespace NASA_Space_Apps_Challenge {


    // Helper class to avoid keys exposure to GitHub

    public class APIKeys {

        [JsonIgnore]
        public static APIKeys? Instance = null;
        public static string JsonVersion = "1.0.0";


        private static string basePath = ""; // additional path to file added to BaseDirectory
        private static string baseFileName = "api_keys"; // expected file name
        
        public static string GetFullPath() { return Path.Combine(AppContext.BaseDirectory, basePath, baseFileName+".json"); }


        public static void ReloadKeys() {

            var fullPath = APIKeys.GetFullPath();


            if (!File.Exists(fullPath)) {

                File.WriteAllText(fullPath, JsonConvert.SerializeObject(new APIKeys(), Formatting.Indented));
 
            }
            



            var preload = JsonConvert.DeserializeObject<APIKeys>(File.ReadAllText(fullPath));

            if (preload.Version!= APIKeys.JsonVersion) {

            }
        }


        public string EARTH_LOGIN_TOKEN = "";
        public string GOOGLE_API_KEY = "";
        public string Version = APIKeys.JsonVersion;
    }
}
