Bu soruyu gerçek hayatta nasıl yapacaksam öyle kurdum. Amacım: **5 dakikada bir** sipariş API’sini çağıran bir servis yazmak, çağrıdan önce **access token** almak, token’ın süresi dolmadan **erken yenilemek** ve **token alma isteğini saatte en fazla 5 kere** yapmak.

Bunun için iki küçük proje hazırladım:

* **OrderPoller.Worker** → arka planda çalışan worker.
* **MockAuthOrdersApi** → test edebilmem için sahte (mock) auth + orders API’si.

---

## Ne yaptım?

* **Worker (BackgroundService)**: Her turda siparişleri çekiyor. Varsayılan periyot **300 sn**.
* **TokenProvider**:

  * Token’ı **hafızada** tutuyorum. Süre dolmadan biraz önce **otomatik yeniliyorum**.
  * Aynı anda birden fazla thread token almasın diye **SemaphoreSlim** kullandım.
  * Son 1 saatte kaç kez token aldığımı **kuyrukta tutuyorum**; sayı 5’e dayanırsa yeni token istemiyorum.
* **OrdersClient**: `IHttpClientFactory` ile üretilen `HttpClient`. Her istekten önce `TokenProvider`dan token alıp `Authorization: Bearer` ekleyerek `/orders`’ı çağırıyor.
* **MockAuthOrdersApi**:

  * **/oauth/token**: `client_credentials` akışı; `{ token_type, expires_in, access_token }` döndürüyor.
  * Aynı **client\_id** için **saatte max 5** token kuralı var. 6’ncıya **429** veriyor.
  * **/orders**: Bearer’sız 401, geçerli token ile 200.
  * Swagger açık; “Authorize” ile çok rahat test ediliyor.

---

## Kullandığım Teknolojiler ; 

* .NET **Worker Service** (BackgroundService)
* **IHttpClientFactory**
* **Options pattern** ile ayarlar (Auth / Api / Polling)
* **SemaphoreSlim** (thread-safe yenileme)
* **Swagger/OpenAPI**

---

## Cevap ; 

* **“5 dakikada bir sipariş listesi”**
  Periyodu `Polling:PeriodSeconds` ile veriyorum (default 300). Worker her turda `OrdersClient.GetOrdersAsync()` çalıştırıyor.

* **“Çağrıdan önce token”**
  `OrdersClient` her seferinde `TokenProvider.GetTokenAsync()` çağırıyor. Token hâlâ geçerliyse cache’den veriyorum, değilse yeniliyorum.

* **“Saatte 5 token isteği”**
  İki taraftan güvence:

  1. **Erken yenileme** (örn. bitmeden 60 sn önce) → panik yenilemeleri azaltıyor.
  2. **Saatlik sayaç** → 5’e gelince yeni token talebini kestim (log + exception).
     Mock API de aynı limiti sunucu tarafında uyguluyor.

---

## Akış şu şekilde ; 

1. Worker periyodik çalışır.
2. `OrdersClient` → `GetOrdersAsync()`
3. `TokenProvider`:

   * Token geçerli mi? **Evet** → devam.
   * **Hayır** → Semaphore ile kilitle, saatlik sayaca bak. Limit dolmak üzereyse **token isteme**; değilse `/oauth/token`’dan yeni token al, süresini hesapla.
4. `Authorization: Bearer …` ile **/orders** çağrılır, siparişler gelir.

---

## Küçük ekstralar ;

* Swagger’dan uçtan uca **manuel test** daha rahat oluyor.
* Tüm ayarlar **appsettings**’te; tek yerden yönetiyorum.
* Yenilemeyi **erken** yaptığım için expiry’de sorun yaşamıyorum.
* Token alma kısmı **tek thread** ile yapılıyor.

---

## Worker için ayarlar ; 

`OrderPoller.Worker/appsettings.json` örneği:

```json
{
  "Auth": {
    "TokenUrl": "http://localhost:5299/oauth/token",
    "ClientId": "sample-client",
    "ClientSecret": "sample-secret",
    "Scope": "orders.read"
  },
  "Api": {
    "BaseUrl": "http://localhost:5299",
    "OrdersPath": "/orders"
  },
  "Polling": {
    "PeriodSeconds": 300,
    "TokenRefreshSkewSeconds": 60,
    "MaxTokenRequestsPerHour": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

> Not: Limite takılmamak için mock API’de `expires_in`’i **900 sn (15 dk)** tuttuğumda worker saat başına 4’ten fazla token almıyor.

---

## MockAuthOrdersApi – Endpoint’ler ; 

* **POST `/oauth/token`**
  Form: `grant_type=client_credentials`, `client_id`, `client_secret` (+ opsiyon `scope`)
  Dönen:

  ```json
  { "token_type": "Bearer", "expires_in": 900, "access_token": "..." }
  ```

  Limit aşımı → 429, hatalı client → 401

* **GET `/orders`**
  `Authorization: Bearer <token>` zorunlu. Geçersiz/expired → 401, geçerli → 200

Swagger: `http://localhost:5299/swagger`

1. Önce `/oauth/token`’ı “Try it out” ile çalıştır, token al.
2. Sağ üst **Authorize** → çıkan kutuya **yalnızca access\_token**’ı yaz (Bearer yazma).
3. `/orders` → Execute → 200.

---

## Nasıl çalıştırıyorum?

1. **Mock API**

   ```bash
   cd MockAuthOrdersApi
   dotnet run
   ```

   `http://localhost:5299/swagger` açık olacak.

2. **Worker’ı başlatıyorum** (ayrı terminal)

   ```bash
   cd OrderPoller.Worker
   dotnet run
   ```

   Konsolda ; 

   * “Order poller başladı. Periyot: …”
   * İlk turda **POST /oauth/token**
   * Ardından “Siparişler çekildi …”
   * Token bitmeden kısa süre önce otomatik yenileme log’u
   * Limit aşılırsa uyarı/log; bir sonraki turda tekrar dener.
