# AI yapılandırması

`AppConfig.cs` içinde `CentralApiUrl` alanına tam HTTPS chat-completions
adresini, `CentralModels` dizisine sağlayıcının model kimliklerini ekleyin.
Bu alanlar bilerek boş bırakılmıştır. Liste sırası açılır liste sırasıdır.
URL credential, query veya fragment içeremez. Token'ı kaynak koda koymayın.

Outlook şeridindeki **AI Ayarları** üzerinden Central/Legacy, Central model
ve Central token ayarlanır. İlk kurulumda Central seçilidir. Liste doluysa
ayarlar penceresi ilk modeli önerir; Kaydet ile seçimi kalıcılaştırın.
Token alanını temizleyip Kaydet'e basmak kayıtlı token'ı siler. İptal/çarpı
kaydetmez. Eksik Central yapılandırması çağrıdan önce anlaşılır hata verir.

Ayarlar `%LOCALAPPDATA%\Askai\settings.xml` içinde tutulur. Token Windows
DPAPI CurrentUser ile şifrelenir; başka bir Windows kullanıcısına taşınamaz.
Model ve sağlayıcı gizli değildir. Okunamayan kayıt için ayarlar penceresi
uyarı verip yeniden kaydetmeye izin verir. Central token Legacy'ye gönderilmez.
Legacy mevcut `ApiUrl`, `ApiKey` ve istek formatını kullanır.

Central `model`, `messages`, `stream: true` gönderir ve OpenAI uyumlu SSE
`choices[].delta.content` parçalarını bellekte biriktirir; popup yalnızca ilerlemeyi gösterir. Servis SSE ve
`[DONE]` bitiş işaretini desteklemelidir; AW Center'ın non-streaming kullanımı
tek başına bu desteği kanıtlamaz. Otomatik retry/fallback/redirect yapılmaz.
İstek ve toplam akış sınırı 1 MiB; toplam süre `TimeoutMinutes` ile sınırlıdır.
Akış hatası veya iptalde kısmi içerikten Outlook yanıtı oluşturulmaz.

## Doğrulama

`dotnet run --project tests/TransportTests.csproj` ağ erişimi yapmadan Central
transport sözleşmesini kontrol eder (.NET 10 SDK). Üretim hedefi .NET Framework
4.7.2/VSTO olarak korunmuştur.

Windows'ta Visual Studio Office geliştirme araçlarıyla çözümü derleyin.
Outlook'ta Central/Legacy seçimini, model/token kaydetmeyi, yeniden açmayı,
token silmeyi, hazırlık durumunu ve bekleyen akışın iptalini doğrulayın.
Canlı denemede hassas içerik yerine uydurma bir e-posta kullanın.

## Bağlantı hatalarını ayırma

Arayüz artık servis hatalarında HTTP kodunu ve güvenli bir açıklamayı gösterir:
401 token, 403 yetki, 407 proxy kimlik doğrulaması, 429 kullanım sınırı; 3xx ise
izlenmeyen yönlendirme olarak bildirilir. 200 ile HTML dönmesi başarılı AI yanıtı
sayılmaz; giriş veya proxy sayfası olabileceği belirtilir. Central'da belirtilen
Content-Type `text/event-stream` olmalıdır; `application/json` gibi farklı türler
streaming uyumsuzluğu olarak bildirilir. Header'ı eksik, geçerli SSE akışları önceki
sürümlerle uyumlu biçimde kabul edilir.

Windows `TrustFailure` hatası sertifika doğrulaması, `SecureChannelFailure` veya
`AuthenticationException` TLS bağlantısı; isim çözümleme hataları DNS olarak
gösterilir. Bunlar kesin alt nedeni belirleyen sertifika analizi değildir; Windows
ve servis tarafında ilgili zincir/proxy/TLS ayarları ayrıca kontrol edilmelidir.
Ham servis yanıtı, yönlendirme adresi, token veya iç exception mesajı ekrana/loga
yazılmaz. İç exception tanı için korunur; sertifika doğrulaması kapatılmaz. Alt
hatada `Win32Exception` varsa yalnızca sayısal Windows hata kodu da gösterilir;
ham hata metni ekrana yazılmaz.

Curl ile yapılan GET isteğinde 200 alınması, uygulamanın token, model ve
`stream: true` içeren POST çağrısının başarılı olacağını kanıtlamaz. Testler HTTP,
yanıt türü ve bağlantı hatalarının güvenli sınıflandırmasını ağ kullanmadan kontrol
eder; gerçek Windows TLS ortamını taklit etmez.

### PowerShell gerektirmeyen TLS uyumluluk denemesi

**AI Ayarları → TLS 1.2 uyumluluk modu → Kaydet** ile sonraki AI isteğinde TLS 1.2
denenebilir. Varsayılan kapalıdır; eski ayar kayıtlarında alan yoksa kapalı kabul
edilir. Central ve Legacy HTTPS bağlantılarına uygulanır. Token, model ve istek
formatı değişmez. Yeniden başlatma gerekmez; mod değişince farklı bağlantı havuzu
kullanılır. Devam eden istek başlangıçta seçilen modu kullanır.

Bu seçenek yalnızca AI HTTP istemcisinde `HttpClientHandler.SslProtocols = Tls12`
ayarlar. Outlook'un `ServicePointManager` ayarı, Windows/registry ayarları, proxy
kullanımı ve sertifika doğrulaması değiştirilmez. Central ve Legacy bağlantı ve
cookie havuzları ayrı tutulur. Yeni bağımlılık veya otomatik retry eklenmez.

Açıkken TLS 1.3 denenmez; servis TLS 1.2 desteklemelidir. Kapalıyken önceki TLS
seçimi kullanılır. Bu bir uyumluluk denemesidir; kök nedenin doğrulandığı veya
bağlantının düzeldiği anlamına gelmez. Sertifika zinciri ya da kurumsal proxy
sorunu sürerse mod bunu atlatmaz; Windows hata koduyla BT/servis yöneticisinden
destek alınmalıdır.

Otomatik testler modun TLS 1.2 seçtiğini, manuel sertifika seçilmediğinde özel
doğrulama callback'i eklenmediğini, proxy kullanımının korunduğunu ve havuzların ayrıldığını kontrol
eder. Windows'ta ayrıca seçeneği açıp kaydetmeyi, ayarlar penceresini yeniden
açmayı, eski ayar kaydını yüklemeyi, modu kapatmayı ve gerçek API çağrısını
doğrulayın. DPAPI ayar kaydı, VSTO arayüzü ve gerçek TLS el sıkışması bu macOS
test ortamında doğrulanmaz.

### Manuel Central sunucu sertifikası

**AI Ayarları → Central sunucu sertifikası → Sertifika seç… → Kaydet** ile BT'den
alınan sunucu sertifikası seçilebilir. Desteklenen dosyalar en fazla 64 KiB olan,
tek bir açık sunucu sertifikası içeren DER veya PEM `.cer/.crt` dosyalarıdır.
PEM'de UTF-8 BOM desteklenir. PFX/P12, özel anahtar ve çoklu sertifika paketleri
desteklenmez; parola alanı veya istemci/mTLS sertifikası eklenmez.

Arayüz sunucu adını, son geçerlilik tarihini ve SHA-256 parmak izini gösterir.
Kaydet yalnızca sertifikanın açık DER verisini ayarlara ekler; dosya yolu ve özel
anahtar kaydedilmez. Token'ın DPAPI şifrelemesi korunur. Seçilen dosyanın daha
sonra değiştirilmesi ayarı değiştirmez; yeni sertifika arayüzden yeniden seçilip
kaydedilmelidir. İptal/pencereyi kapatma değişiklikleri kaydetmez.

Seçim yapılınca Central bağlantısında sunucunun sunduğu **sertifikanın tamamı**
seçilen sertifikayla birebir eşleşmelidir. Windows başka bir sertifikaya güvense
bile farklı sertifika kabul edilmez. Sertifika sunucuda yenilendiğinde yeni
sertifikayı seçin. Bu alan kök/ara CA'ya genel güven vermek için değildir;
bağlanılan sunucunun sertifikasını seçin. Legacy bu ayarı kullanmaz.

Eşleşen sertifika için yalnızca `UntrustedRoot` / `PartialChain` güven zinciri
eksiklikleri yerel açık güvenle karşılanır. Adres uyuşmazlığı, geçersiz süre,
sunucu kullanımına uygun olmayan EKU/key usage, imza, iptal, bilinmeyen veya
diğer zincir hataları kabul edilmez. Manuel seçimde HTTP istemcisi Windows'tan
iptal kontrolü ister (`CheckCertificateRevocationList = true`); CRL/OCSP kontrolü
belirsiz veya çevrimdışı kalırsa bağlantı reddedilir. Böyle bir durumda iptal
kontrolü altyapısının erişimini BT/servis yöneticisiyle doğrulayın.

Windows sertifika deposu, registry, kurumsal proxy veya Outlook'un genel TLS
ayarları değiştirilmez. TLS 1.2 uyumluluk modu ayrı bir seçenektir; sunucu
sertifikası seçimi protokol/cipher uyumsuzluğunu çözmez. **Temizle → Kaydet** normal
Windows güven doğrulamasına döndürür. Önceki ayar kayıtlarında sertifika alanı
yoksa manuel güven etkin değildir.

Sertifika/TLS modu başına ayrı havuz kullanılır. Mevcut isteği iptal etmeden yeni
ayarlar sonraki isteğe uygulanır; seçili sertifikanın süresi her istekte yeniden
kontrol edilir. Kaynakları sınırlamak için bir Outlook oturumunda en fazla 16
farklı manuel sertifika/TLS havuzu oluşturulur; limite ulaşılırsa arayüz yeniden
başlatma gerektiğini bildirir.

Testler üretilmiş açık sertifikalarla birebir eşleşmeyi, yanlış adres bayrağını,
tarih/EKU/zincir hata kısıtlarını, DER/PEM okumayı, dosya limitlerini, handler'da
iptal kontrolünün açılmasını ve havuz ayrımını doğrular. macOS sandbox'ında native
SecTrust API'si kullanılamadığı için sertifika testleri normal macOS erişimiyle
çalıştırılır; güven deposuna sertifika eklenmez. Bunlar gerçek Windows Schannel,
CRL/OCSP erişimi veya canlı API doğrulaması yerine geçmez.

Windows'ta ayrıca sertifika seç/kaydet/yeniden aç/temizle, eski ayar kaydı,
sertifika yenilenmesi, adres uyuşmazlığı, iptal edilmiş sertifika ve erişilemeyen
iptal kontrolü senaryolarını gerçek test servisiyle doğrulayın.

## Mail yetenekleri

- **AI ile mail hazırla:** Ana Outlook penceresindeki AI Asistan grubunda bulunur.
  Konu zorunlu, açıklama/talimat isteğe bağlıdır. Onaydan sonra yeni bir mail
  penceresi açılır. Konu kullanıcı girdisinden alınır; alıcıları kullanıcı belirler.
- **Seçili metni iyileştir:** Yeni mail veya yanıt taslağını ayrı bir pencerede
  açın, metni seçin ve AI Asistan grubundaki düğmeyi kullanın. Yalnızca seçilen
  metin yeniden yazılır. Seçimin dışındaki içerik, imza, alıntılar ve ekler korunur.
  Baştaki/sondaki paragraf sınırları değiştirilmez. Seçilen alanın kelime bazındaki
  zengin biçimlendirmesi birebir korunmayabilir. Tablo, görsel, alan veya korumalı
  içerik içeren seçimler desteklenmez. Satır içi yanıt için önce ayrı pencereye açın.
- **AI ile yanıtla / tümünü yanıtla:** Ana pencerede tek bir mail seçerek veya
  okuma penceresinde kullanılır. Yanıtın amacını isteğe bağlı olarak belirtin.
  Onaydan sonra Outlook'un kendi Reply/ReplyAll taslağı açılır; alıcılar ve yazışma
  ilişkisi Outlook tarafından belirlenir. Kaynak mail değiştirilmez.

Üç yetenekte de popup hazırlık durumunu ve **Mail’e aktar / Vazgeç** seçeneklerini
gösterir; üretilen mail içeriğini göstermez. İçerik tamamen hazır olmadan aktarım
düğmesi etkinleşmez. **Mail’e aktar** açık aktarım onayıdır; gönderim onayı değildir.
Uygulama hiçbir zaman mail göndermez. Aktarılan metni Outlook editöründe inceleyip
düzenleyin; gönderimi yalnızca Outlook üzerinden kendiniz yapın. İmza ve önceki
yazışma Outlook tarafından oluşturulduktan sonra AI metni bunların önüne eklenir.

İptal, vazgeçme, popup'ı kapatma, boş yanıt ve üretim hatası maili değiştirmez.
Aktarım sırasında yeni taslak açıldıktan sonra hata oluşursa uygulama yalnızca
kendi oluşturduğu taslağı kaydetmeden kapatmayı dener; kapanamazsa kullanıcıyı
bilgilendirir. Mevcut kaynak maili veya kullanıcı taslağını silmez.

Aynı anda yalnızca bir AI işlemi çalışır. İşlem başka bir mail seçilse bile
başlangıçtaki hedefe bağlı kalır. İyileştirme sırasında hedef kapatılır, gönderilir
veya gövdesi/konusu değiştirilirse sonuç uygulanmaz; yeniden hazırlamak gerekir.
Konu en fazla 255, talimat 4.000, kaynak metin 32.000 karakter olabilir. Uzun kaynak
sessizce kesilmez; kısaltılması istenir. Kaynak ve talimat seçili AI sağlayıcısına
gönderilir; uygulama bunları dosyaya veya loga yazmaz.

### Legacy tamamlanma sözleşmesi

Legacy istek alanları korunur. SSE yanıtı `[DONE]`, NDJSON yanıtı `done: true`
ile tamamlanmalıdır. `application/json` yanıtı tek ve tamamlanmış bir JSON
belgesi olmalıdır; `response`, `text`, `content`, `output`, `message.content`
ve `choices[].message.content` desteklenir. Açık `done: false` veya streaming
`delta` içeren tek JSON belge tamamlanmış sayılmaz.

Bozuk JSON, hata olayı, işaretsiz kesilen akış ve `finish_reason` / `done_reason`
alanında `stop` dışındaki bitiş nedenleri reddedilir. Central da başarısız bitiş
nedenlerini reddeder. Önceden kabul edilen ham metin veya yalnızca bağlantının
kapanmasıyla biten Legacy akışları artık kabul edilmez; sağlayıcı bu sözleşmeye
uymalıdır. Bu, eksik içeriğin tamamlanmış mail olarak aktarılmasını önler.
Her iki sağlayıcıda istek/yanıt sınırı 1 MiB; toplam süre `TimeoutMinutes` kadardır.

### Yeni doğrulama kapsamı

`dotnet run --project tests/TransportTests.csproj` artık mail onay durumlarını,
paragraf sınırlarını ve Legacy akışlarını da kontrol eder. Ayrıca gerçek aktarım
sınıfını test nesneleriyle çalıştırarak hedef/aralık seçimini, değişmiş hedefin
reddedilmesini ve yeni taslak hata temizliğini sınar. Ağ veya Outlook gerektirmez.
Bu simülasyonlar gerçek COM ömrünü, Word biçimlendirmesini, Outlook otomatik
kaydetmesini veya Windows arayüzünü doğrulamaz. Test projesindeki CA1416 istisnası
yalnızca bağlı Windows adaptörünün gerçek COM nesnesi kullanılmayan bu çalıştırıcısı içindir.

Windows üzerinde Visual Studio'nun **Office/SharePoint development** araçlarıyla
`Askai.sln` çözümünü derleyip klasik Outlook'ta şu kontrolleri yapın:

1. Ana pencere, mail okuma ve taslak pencerelerinde uygun yeteneklerin görünmesi;
   AI Ayarları'nın ve Central/Legacy seçiminin çalışması.
2. Konudan hazırlamada onaydan önce taslak oluşmaması; onaydan sonra boş alıcılar,
   doğru konu, tek AI gövdesi ve mevcut imza ile yeni mail açılması.
3. Yanıtla/tümünü yanıtla için doğru kaynak, alıcılar, imza, yazışma ilişkisi ve
   alıntıların korunması; talimatlı ve talimatsız üretim.
4. HTML, düz metin ve RTF taslaklarda seçili metnin değiştirilmesi; seçili paragrafın
   son işareti dahil olsa bile sonraki imza/alıntının ayrı paragrafta kalması.
   Seçim dışındaki biçim, ekler ve gömülü görsellerin korunması.
5. Birden fazla mail penceresi açıkken doğru hedefe aktarım; seçim değişse bile
   ilk aralığın kullanılması; hedefte yazı/biçim/konu değişikliği, kapanma ve kullanıcı
   gönderimi sonrasında aktarımın engellenmesi.
6. Boş/karmaşık seçim, birden fazla mail seçimi, mail olmayan öğe, uzun kaynak,
   API hatası, eksik akış, iptal, çarpı, vazgeçme ve çift onay senaryoları.
7. Aktarım başarısızlığında yalnızca yeni oluşturulan taslağın kapatılması;
   kullanıcıya ait taslakların açık ve değişmeden kalması.
8. Klavye ile düğmelere erişim, yüksek DPI'da okunabilirlik, popup'ta metin
   önizlemesinin bulunmaması ve normal Outlook gönderiminin kullanıcı kontrolünde olması.
