using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NASA_Space_Apps_Challenge
{
    public partial class Form1 : Form {


        // GeoJSON bbox в системе координат WGS84(EPSG:4326).
        // Порядок : minLon, minLat, maxLon, maxLat = запад, юг, восток, север.
        // Единицы — десятичные градусы(точка как разделитель), диапазоны: lon[-180..180], lat[-90..90].

        // Пример(Нидерланды, небольшой бокс) :
        // [5.70, 52.60, 5.90, 52.75]


        private double minLon = 5.70;
        private double minLat = 52.60;
        private double maxLon = 5.90;
        private double maxLat = 52.75;

        public Form1() {
            InitializeComponent();
        }

        /* EARTH LOGIN TOKEN */

        public readonly string EARTH_LOGIN_TOKEN = 
            "eyJ0eXAiOiJKV1QiLCJvcmlnaW4iOiJFYXJ0aGRhdGEgTG9naW4iLCJzaWciOiJlZGxqd3RwdWJrZXlfb3BzIiwiYWxnIjoiUlMyNTYifQ.eyJ0eXBlIjoiVXNlciIsInVpZCI6ImFzY2VnIiwiZXhwIjoxNzY0NzI0Mzg3LCJpYXQiOjE3NTk1NDAzODcsImlzcyI6Imh0dHBzOi8vdXJzLmVhcnRoZGF0YS5uYXNhLmdvdiIsImlkZW50aXR5X3Byb3ZpZGVyIjoiZWRsX29wcyIsImFjciI6ImVkbCIsImFzc3VyYW5jZV9sZXZlbCI6M30.zPuUBGNgjzzeIbXzQu2Tt2B5XSG-VWUfQa8229fjLzEcRIi2dGKKIqofs1oFksOuYIKHky43WD8y6oce8yrIi4YgV5_5yjdDpeL2T7SMdmYbZxMUsI1rCYFRUuz3IBO8bJCE9E3JPqY9wewtTmY0XxjVKTBRkCu-q8Okzh-VZkoUlypnGgF_RvbEKt6DFVIrLmkVhYslIJ_ipdngLNSgVVV-ZeKZ3uT--h7NdhPcryukNWY5hkQ8mlfOjdhWYgBTz0RFPsLkEh7Pf0Ynk1EE7QS-Q_w_kl-I_pBNkYmE4SglS4PF-5s0PgmfH6iKC0ST91UB5605L5gL2pa7gcUBAQ";


        async void GetHLSData() {


            // Готовим клиент и адрес STAC-поиска
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", EARTH_LOGIN_TOKEN);


            var start = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var endExcl = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc); // 1 июня 00:00Z, эксклюзивно



            // Собираем тело запроса
            // collections: какие наборы хотим (HLS S2 и HLS Landsat).
            // bbox: прямоугольник WGS84 в порядке minLon,minLat,maxLon,maxLat.
            // datetime: интервал дат(ISO 8601).
            // limit: максимум элементов за один ответ(остальные придут через пагинацию links.next — здесь не обработано).

            var body = new {
                //  Ищем в коллекциях HLSS30 и HLSL30 с фильтром облачности
                // для Sentinel-2 (HLSS30) NIR — это B08,
                // для Landsat(HLSL30) NIR — это B05.
                collections = new[] { "HLSS30.v2.0", "HLSL30.v2.0" },
                // collections = new[] { "HLSS30", "HLSL30" },
                bbox = new[] { minLon, minLat, maxLon, maxLat },
                // use RFC3339 or ISO8601 formatted datetime strings
                datetime = $"{start:yyyy-MM-ddTHH:mm:ssZ}/{endExcl:yyyy-MM-ddTHH:mm:ssZ}",
                //datetime = "2025-04-01/2025-05-31",
                limit = 200
            };

          

            var bodyJson = JsonSerializer.Serialize(body);
            var req = new StringContent(bodyJson, Encoding.UTF8, "application/json");


            // теперь можно делать и поиск по STAC, и скачивание assets
            // отправляем запрос; если всё ок — забираем ответ как строку JSON.

            var searchUrl = "https://cmr.earthdata.nasa.gov/stac/LPCLOUD/search";
            var resp = await http.PostAsync(searchUrl, req);



            var respText = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) {
                throw new Exception($"STAC 400/..: {resp.StatusCode}\n{respText}");
            }



            //resp.EnsureSuccessStatusCode();
            //var json = await resp.Content.ReadAsStringAsync();


            // парсим
            using var doc = JsonDocument.Parse(respText);
            var features = doc.RootElement.GetProperty("features");

            // собираем ссылки на ассеты
            var hrefs = new List<(string id, string url, string band)>();
            foreach (var f in features.EnumerateArray()) {
                var id = f.GetProperty("id").GetString();
                var assets = f.GetProperty("assets");

                // HLS v2: обычно есть B02,B03,B04, и NIR: B08 (S2) или B05 (L8/9).
                // Маска качества чаще называется "Fmask" (а не QA).
                foreach (var band in new[] { "B02", "B03", "B04", "B08", "B05", "Fmask" }) {
                    if (assets.TryGetProperty(band, out var a) &&
                        a.TryGetProperty("href", out var href)) {
                        hrefs.Add((id!, href.GetString()!, band));
                    }
                }
            }



            /*


            // Парсим ответ и достаём ссылки на файлы(assets)
            // распарсить features → взять assets["B03","B04","B08"/"B05","B02","QA"]

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var features = doc.RootElement.GetProperty("features");
            var hrefs = new List<(string id, string url, string band)>();


            // в STAC каждый feature — это одна сцена/тайл.
            foreach (var f in features.EnumerateArray()) {

                // у неё есть словарь assets: ключи — названия полос (B02/B03/B04/…/QA),
                // значения — метаданные, где href — прямая ссылка на файл (обычно COG GeoTIFF).
                var assets = f.GetProperty("assets");
                foreach (var band in new[] { "B02", "B03", "B04", "B08", "B05", "QA" }) {

                    // мы проходим по сценам, проверяем, есть ли нужная полоса, и собираем (id, url, band) в список hrefs

                    if (assets.TryGetProperty(band, out var a) && a.TryGetProperty("href", out var href))
                        hrefs.Add((f.GetProperty("id").GetString(), href.GetString(), band));
                }
            }

            // что дальше с hrefs
            // — у вас на руках прямые URL на нужные файлы(B02 / B03 / B04 / NIR / QA).
            // Их можно скачивать и класть в data/, затем резать по bbox и считать индексы.

            /*
             
            если сервер вернёт 401/403, нужен Earthdata Login (куки/токен).
            чтобы отфильтровать облачность в самом поиске, можно добавить query (например, {"eo:cloud_cover":{"lt":40}}).
            результаты могут быть разбиты на страницы; для полного набора проверьте в ответе links → rel:"next" и повторяйте запрос.
            строка stacUrl = ".../collections/search" вам тут не нужна — реальная работа идёт через .../search.

             * */



        }


        static async Task<string> AppeearsLoginAsync(string user, string pass) {
            using var http = new HttpClient();
            var byteCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            var req = new HttpRequestMessage(HttpMethod.Post,
                "https://appeears.earthdatacloud.nasa.gov/api/login");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", byteCreds);
            req.Content = new StringContent("grant_type=client_credentials",
                Encoding.UTF8, "application/x-www-form-urlencoded");

            var resp = await http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("token").GetString()!; // bearer
        }



        private void button1_Click(object sender, EventArgs e) {
            GetHLSData();

            //var token = AppeearsLoginAsync("asceg", "In345-523-234Code");

            //File.WriteAllText("appeears_token", token.Result);

        }
    }
}
