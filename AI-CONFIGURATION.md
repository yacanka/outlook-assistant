# AI yapılandırması

`AppConfig.cs` içinde `CentralApiUrl` alanına tam HTTPS chat-completions
adresini, `CentralModels` dizisine sağlayıcının model kimliklerini ekleyin.
Bu alanlar bilerek boş bırakılmıştır. Liste sırası açılır liste sırasıdır.
URL credential, query veya fragment içeremez. Token'ı kaynak koda koymayın.

Outlook şeridindeki **Ayarlar** üzerinden PLUS AI/TAB 3.2, PLUS AI model
ve PLUS AI token ayarlanır. İlk kurulumda PLUS AI seçilidir. Liste doluysa
ayarlar penceresi ilk modeli önerir; Kaydet ile seçimi kalıcılaştırın.
Model seçici yalnız birden çok model tanımlandığında gösterilir. Tek model
gizli alanda otomatik seçilir ve Kaydet ile saklanır; boş listede servis
yapılandırmasının eksik olduğunu bildiren mevcut doğrulama korunur.
Token alanını temizleyip Kaydet'e basmak kayıtlı token'ı siler. İptal/çarpı
kaydetmez. Eksik PLUS AI yapılandırması çağrıdan önce anlaşılır hata verir.

Ayarlar `%LOCALAPPDATA%\Askai\settings.xml` içinde tutulur. Token Windows
DPAPI CurrentUser ile şifrelenir; başka bir Windows kullanıcısına taşınamaz.
Model ve sağlayıcı gizli değildir. Okunamayan kayıt için ayarlar penceresi
uyarı verip yeniden kaydetmeye izin verir. PLUS AI token TAB 3.2'ye gönderilmez.
TAB 3.2 mevcut `ApiUrl`, `ApiKey` ve istek formatını kullanır.

PLUS AI `model`, `messages`, `stream: true` gönderir ve OpenAI uyumlu SSE
`choices[].delta.content` parçalarını bellekte biriktirir; popup metni parçalar geldikçe salt okunur önizlemede gösterir. Servis SSE ve
`[DONE]` bitiş işaretini desteklemelidir; AW Center'ın non-streaming kullanımı
tek başına bu desteği kanıtlamaz. Otomatik retry/fallback/redirect yapılmaz.
İstek ve toplam akış sınırı 1 MiB; toplam süre `TimeoutMinutes` ile sınırlıdır.
Akış hatası veya iptalde kısmi içerikten Outlook yanıtı oluşturulmaz.

## Doğrulama

`dotnet run --project tests/TransportTests.csproj` ağ erişimi yapmadan PLUS AI
transport sözleşmesini kontrol eder (.NET 10 SDK). Üretim hedefi .NET Framework
4.7.2/VSTO olarak korunmuştur.

Windows'ta Visual Studio Office geliştirme araçlarıyla çözümü derleyin.
Outlook'ta PLUS AI/TAB 3.2 seçimini, model/token kaydetmeyi, yeniden açmayı,
token silmeyi, hazırlık durumunu ve bekleyen akışın iptalini doğrulayın.
Canlı denemede hassas içerik yerine uydurma bir e-posta kullanın.

## Bağlantı hatalarını ayırma

Arayüz artık servis hatalarında HTTP kodunu ve güvenli bir açıklamayı gösterir:
401 token, 403 yetki, 407 proxy kimlik doğrulaması, 429 kullanım sınırı; 3xx ise
izlenmeyen yönlendirme olarak bildirilir. 200 ile HTML dönmesi başarılı AI yanıtı
sayılmaz; giriş veya proxy sayfası olabileceği belirtilir. PLUS AI'da belirtilen
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

### TLS 1.2 açıkken HTTP 405 alınması

TLS 1.2 açıkken HTTP 405 yanıtı alınması, bu denemenin HTTP katmanına kadar
ilerlediğini gösterir. Yanıt API sunucusundan, gateway'den veya proxy'den gelebilir;
gerçek model servisine ulaşıldığını tek başına kanıtlamaz. TLS 1.2'yi açık tutup
POST hedefini inceleyin. 405 tanısı tam API endpoint'ini kontrol etmeyi önerir ve
varsa `Allow` başlığındaki yalnızca bilinen HTTP yöntemlerini gösterir. Ham yanıt,
URL veya bilinmeyen header değerleri gösterilmez.

Outlook, `AppConfig.CentralApiUrl` adresine doğrudan POST yapar; otomatik olarak
`/chat/completions` eklemez veya yönlendirme izlemez. AW Center'ın çalışan
sağlayıcı ayarındaki tam URL (host, port, path ve sondaki slash dahil) ile bu
adresi karşılaştırın. Web ana sayfası, model listeleme yolu veya AW Center'ın kendi
frontend adresi sağlayıcının chat-completions endpoint'i yerine geçmez.

Yerel AW Center `integrations.ai` istemcisi de POST, Bearer ve redirect kapalı
kullanır; model isteği `stream: false`, `Accept: application/json` şeklindedir.
Python `requests.post(..., stream=True)` burada yalnızca HTTP gövdesinin parça
parça okunmasını seçer; model token streaming'i açmaz. Outlook ise `stream: true`
ve `Accept: text/event-stream` kullanır. POST/endpoint sorunu düzeldikten sonra
sağlayıcının SSE desteğini ayrıca doğrulayın; 405 tek başına streaming desteği
hakkında kesin sonuç vermez.

TLS 1.2 kapalıykenki TLS hatası ayrı incelenmelidir. .NET Framework/Outlook
host'unun ve Windows'un varsayılan protokol seçimi, Python/OpenSSL ile aynı
olmayabilir. TLS 1.2 açıkken HTTP yanıtı alınabiliyorsa, bu seçenekle ilerleyin;
sertifika doğrulamasını veya host'un genel TLS ayarını değiştirmek 405'i çözmez.

### PowerShell gerektirmeyen TLS uyumluluk denemesi

**Ayarlar → Gelişmiş → TLS 1.2 uyumluluk modu → Kaydet** ile sonraki AI isteğinde TLS 1.2
denenebilir. Varsayılan kapalıdır; eski ayar kayıtlarında alan yoksa kapalı kabul
edilir. PLUS AI ve TAB 3.2 HTTPS bağlantılarına uygulanır. Token, model ve istek
formatı değişmez. Yeniden başlatma gerekmez; mod değişince farklı bağlantı havuzu
kullanılır. Devam eden istek başlangıçta seçilen modu kullanır.

Bu seçenek yalnızca AI HTTP istemcisinde `HttpClientHandler.SslProtocols = Tls12`
ayarlar. Outlook'un `ServicePointManager` ayarı, Windows/registry ayarları, proxy
kullanımı ve sertifika doğrulaması değiştirilmez. PLUS AI ve TAB 3.2 bağlantı ve
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

### Servis bazlı manuel sertifika güveni

**Ayarlar → Gelişmiş → Sertifika modu → Sertifika seç… → Kaydet** üzerinden üç
ayrı mod kullanılabilir:

- **CA zincirine güven:** Yalnızca kök ve isteğe bağlı ara CA sertifikaları seçilir.
  Bağlantıda sunucu sertifikasının yüklenen köke uzanan geçerli zinciri aranır.
  Aynı kökten verilen yenilenmiş sunucu sertifikaları da kabul edilir.
- **Sunucu + ara/kök zinciri:** Sunucu sertifikası ile ara ve kök sertifikaları
  birlikte seçilir. Geçerli zincir aynı yüklenen kökte sonlanmalı ve sunucunun
  sunduğu sertifikanın tamamı yüklenen sunucu sertifikasıyla birebir eşleşmelidir.
  Sunucu sertifikası yenilenince bu modda yeniden seçilmelidir. Önceki sürümdeki
  tek sunucu sertifikası seçimi de desteklenmeye devam eder.
- **Sertifika paketi (CRL/OCSP kapalı):** Python `requests` içinde `verify`
  dosyası kullanmaya benzer bir güven modu. Tek dosyada kök, ara ve sunucu
  sertifikaları birlikte bulunabilir; yalnızca kök/ara CA paketi de kabul edilir.
  Tek bir güvenilen köke ulaşan zincir, imza, kullanım amacı, süre ve sunucu
  adresi doğrulanır. Paketteki sunucu sertifikası birebir sabitlenmez; aynı kökten
  verilen yeni bir sunucu sertifikası kabul edilebilir. Paketteki eski sunucu
  sertifikasının süresinin dolması bunu engellemez; süre ve imza kontrolü
  bağlantıda sunulan gerçek sunucu sertifikasına uygulanır. Çevrimiçi CRL/OCSP kontrolü
  yapılmaz; iptal edilmiş, süresi dolmamış sertifikalar tespit edilemeyebilir.
  Bu mod yalnızca kullanıcı seçtiğinde uygulanır; eski modlar ve varsayılanlar
  değişmez. Windows/OpenSSL zincir oluşturma farklılıkları nedeniyle Python ile
  her ortamda birebir aynı sonucu vereceği garanti edilmez.

Python'da çalışan kök + ara + sunucu paketinizi kullanmak için **Ayarlar → Gelişmiş →
Sertifika modu → Sertifika paketi (CRL/OCSP kapalı) → Sertifika seç… →
Kaydet** adımlarını izleyin. PEM içerikli `.cer/.crt` dosyasını seçin. Paket bu
moda özel saklanır; diğer modlarda daha önce yüklenen dosyalar otomatik taşınmaz.

Dosya seçicisinde birden fazla `.cer/.crt` dosyası seçilebilir. DER veya birden
fazla CERTIFICATE bloğu içeren PEM verisi desteklenir; PEM'de UTF-8 BOM kabul
edilir. Dosya uzantısı yalnızca `.cer/.crt` olabilir; `.pem`, PFX/P12, PKCS#7,
özel anahtar ve istemci/mTLS sertifikası desteklenmez. En fazla 16 sertifika,
toplam 256 KiB dosya ve tek sertifika için 64 KiB sınırı vardır. Yinelenen
sertifikalar birleştirilir; dosya sırası sonucu değiştirmez.

Yeni zincir seçimlerinde tek bir kök gerekir. Eksik kök, ilgisiz sertifikalar,
farklı kökler ve geçersiz CA imzaları reddedilir. Paket modunda güven kaynağı
yalnızca CA sertifikalarıdır; dosyadaki sunucu sertifikaları güven kaynağı olarak
kullanılmaz. CA modunda sunucu sertifikası
seçilmez. Sunucu zinciri modunda tam olarak bir sunucu sertifikası gerekir.
CA zinciri seçiminde yalnızca kök de yüklenebilir; bağlantıda ara sertifikalar
sunucudan veya Windows zincir oluşturucusundan tamamlanabilir. Oluşan zincir
mutlaka yüklenen kökte sonlanmalıdır. Alternatif/cross-signed yolların Windows
başka bir kökte sonlandırdığı durumlar da güvenli biçimde reddedilir.

Önce **Servis** sekmesinden PLUS AI veya TAB 3.2 seçin. **Gelişmiş** sekmesindeki
sertifikalar bu servise aittir. Her servisin modu ve üç modun sertifikaları ayrı
saklanır. Servis/mod değiştirmek diğer sertifikaları silmez veya yeniden yorumlamaz.
**Temizle → Kaydet** yalnızca seçili servisin seçili modunu temizler;
aktif modda sertifika yoksa normal Windows doğrulaması kullanılır. Arayüz zincir
sayısını, kök adını ve SHA-256 parmak izini gösterir. Kaydet açık sertifika verisini
saklar; dosya yolu veya özel anahtar kaydedilmez. Token'ın DPAPI şifrelemesi
korunur. Dosyayı sonradan değiştirmek kaydı değiştirmez; yeni dosyalar yeniden
seçilmelidir. İptal/pencereyi kapatma değişiklikleri kaydetmez. Eski ayar kayıtları
sunucu modunda açılır. Eski PLUS AI anahtarları korunur; TAB 3.2 sertifikaları
`LegacyServerCertificate`, `LegacyCaCertificates`, `LegacyBundleCertificates` ve
`LegacyCertificateTrustMode` alanlarında saklanır. Sertifika güveni HTTPS
bağlantısına uygulanır; TAB 3.2 HTTP adresi otomatik olarak HTTPS’e çevrilmez.
Her servisin sertifika/TLS bağlantı havuzu ayrıdır; token ve cookie durumu paylaşılmaz.

Seçim sırasında zincirin yapısı ve imzaları iptal servisine ihtiyaç olmadan
kontrol edilir. Native zincir oluşturucusu AIA sorgusu yapabilir; erişilemeyen
AIA adresleri seçim sırasında beklemeye neden olabilir. Yükleme sırasında
oluşan yolun bütün elemanları seçilen dosyalarda bulunmalıdır. Canlı bağlantıda
sunucu ve CA zinciri modları `Online` iptal kontrolüyle
zinciri yeniden oluşturur. Yalnızca seçilen kökün Windows tarafından tanınmaması
(`UntrustedRoot`) karşılanır; `PartialChain` kabul edilmez. Eski tek sunucu
sertifikası modunda birebir eşleşme koşuluyla önceki `UntrustedRoot` / `PartialChain`
davranışı korunur. Bu iki modda HTTP istemcisinin iptal kontrolü de açıktır;
paket modunda hem HTTP istemcisi hem zincir oluşturucu çevrimiçi iptal
kontrolü yapmaz.
Sunucu ve CA modlarında adres uyuşmazlığı, süre, EKU/key usage, imza, iptal, belirsiz/çevrimdışı iptal
kontrolü ve diğer zincir hataları bağlantıyı reddeder. CRL/OCSP erişimi başarısızsa
BT/servis yöneticisiyle iptal kontrolü altyapısını doğrulayın.

Tek bir CA sertifikasını sunucu modunda seçmek artık yükleme sırasında açık
bir hata verir: **CA zincirine güven** moduna geçip CA dosyalarını yeniden seçin.
Bu kontrol eski kayıtlara da uygulanır; CA sertifikası sunucunun uç sertifikası
olarak karşılaştırılmaz. Gerçek sunucu sertifikasını seçtiyseniz sunucu modunda kalın.

Manuel doğrulama bağlantıyı reddettiğinde genel TLS mesajı yerine adres
uyuşmazlığı, yüklenen sunucu sertifikasının farklı olması, eksik/farklı kök
zinciri veya CRL/OCSP doğrulama hatası gösterilir. Tanı yalnızca ilgili istekte
tutulur; paylaşılan bağlantı havuzunda başka isteklerin hatasıyla karışmaz.
Sertifika callback'ine ulaşmadan oluşan protokol/cipher hatalarında mevcut TLS
mesajı ve varsa Windows hata kodu gösterilmeye devam eder.

Windows sertifika deposu, registry, kurumsal proxy ve Outlook'un genel TLS
ayarları değiştirilmez. TLS 1.2 uyumluluk modu ayrı seçenektir; sertifika seçimi
protokol/cipher uyumsuzluğunu çözmez. TLS seçimi, güven modu ve sertifika parmak
izi başına ayrı bağlantı havuzu kullanılır. Ayar değişikliği devam eden isteği
iptal etmez; sonraki isteğe uygulanır. Bir Outlook oturumunda en fazla 16 farklı
manuel güven/TLS havuzu oluşturulur; limitte yeniden başlatma gerektiği bildirilir.

Testler üretilmiş açık sertifikalarla CA/sunucu modlarını, yenilenmeyi, kök/imza
kontrollerini, adres bayraklarını, DER/çoklu PEM okumayı, dosya sınırlarını,
ayar uyumluluğunu ve havuz ayrımını doğrular. Paket modu testleri gerçek
doğrulama callback’ini çalıştırır; diğer pozitif zincir testleri çevrimdışı
yapı doğrulamasıdır; canlı iptal kontrolünün yerine geçmez. macOS sandbox'ında
native SecTrust API'si kullanılamadığı için sertifika testleri normal macOS
erişimiyle çalıştırılır; güven deposuna sertifika eklenmez. Windows'ta gerçek
Schannel bağlantısı, sertifika seç/kaydet/yeniden aç/temizle, mod değiştirme,
sertifika yenilenmesi, iptal edilmiş sertifika ve erişilemeyen CRL/OCSP senaryoları
ayrıca doğrulanmalıdır.

## Genel mail hatasını ayırt etme

Beklenmeyen mail hataları artık işlem aşamasını (ayar okuma, hazırlık, AI isteği,
önizleme veya Outlook’a aktarım), güvenli hata kategorisini ve hexadecimal hata
kodunu gösterir. Ham exception mesajları, dosya yolları, token ve mail içeriği
gösterilmez veya loglanmaz. Model, token ve endpoint doğrulama hataları kendi
açıklamalarıyla gösterilir. Bu tanı üretim sorununu otomatik olarak çözmez;
hata aşaması ve kodu kök neden araştırması için kullanılmalıdır.

`FileNotFoundException`, `FileLoadException`, `TypeLoadException` veya
`MissingMethodException` görülürse güncel kurulumdaki assembly ve bağımlılıkları
kontrol edin. Son sürüm `Markdig` bağımlılığı içerir; yalnızca `Ask.ai.dll`
dosyasını taşımak yeterli olmayabilir. Windows Visual Studio üzerinde NuGet
restore, Rebuild ve Publish işlemleriyle tam kurulum paketini oluşturun.
Yeni tanı mesajları ancak bu sürüm Outlook’a yüklendikten sonra görünür.

`System.Memory` sürüm çakışması (MSB3277) için proje artık JSON bağımlılığını
`System.Text.Json` 9.0.0 NuGet paketi üzerinden alır. Visual Studio
`PublicAssemblies` klasöründen alınan elle eklenmiş `System.Memory`,
`System.Text.Json` ve `System.Threading.Tasks.Extensions` referansları kaldırılmıştır.
Markdig 1.4.0, `System.Memory` 4.6.3 paketini ister; bu paketin assembly sürümü
4.0.5.0'dır. NuGet ortak bağımlılıkları çözer ve .NET Framework sürüm yönlendirmeleri
derleme sırasında `Ask.ai.dll.config` dosyasına üretilir. Bu dosya da DLL'lerle
birlikte yayımlanmalıdır; Outlook'un genel config dosyasını değiştirmeyin.

Windows'ta güncel projeyle Outlook'u kapatın, NuGet paketlerini geri yükleyin,
Clean Solution ve Rebuild Solution çalıştırın, ardından Publish ile tam paketi
yeniden oluşturup yükleyin. `System.Memory` çakışmasının artık çıkmadığını ve
yayın çıktısında NuGet bağımlılıklarıyla `Ask.ai.dll.config` dosyasının bulunduğunu
kontrol edin. Birden fazla sürümün elle eklenmesi veya eski DLL'lerin tek tek
taşınması aynı problemi yeniden oluşturabilir.

Tanı testlerini sertifika testlerinden bağımsız çalıştırmak için:

```sh
dotnet run --project tests/TransportTests.csproj -- --diagnostics-only
```

## Mail yetenekleri

- **Mail Hazırla:** Ana Outlook penceresindeki AI Asistan grubunda bulunur.
  Konu zorunlu, açıklama/talimat isteğe bağlıdır. Onaydan sonra yeni bir mail
  penceresi açılır. Konu kullanıcı girdisinden alınır; alıcıları kullanıcı belirler.
- **Metni İyileştir:** Yeni mail veya yanıt taslağını ayrı bir pencerede
  açın, metni seçin ve AI Asistan grubundaki düğmeyi kullanın. Yalnızca seçilen
  metin yeniden yazılır. Seçimin dışındaki içerik, imza, alıntılar ve ekler korunur.
  Baştaki/sondaki paragraf sınırları değiştirilmez. Seçilen alanın kelime bazındaki
  zengin biçimlendirmesi birebir korunmayabilir. Tablo, görsel, alan veya korumalı
  içerik içeren seçimler desteklenmez. Satır içi yanıt için önce ayrı pencereye açın.
- **Yanıtla / Tümünü Yanıtla:** Ana pencerede tek bir mail seçerek veya
  okuma penceresinde kullanılır. Yanıtın amacını isteğe bağlı olarak belirtin.
  Onaydan sonra Outlook'un kendi Reply/ReplyAll taslağı açılır; alıcılar ve yazışma
  ilişkisi Outlook tarafından belirlenir. Kaynak mail değiştirilmez.

Üç yetenekte de popup hazırlık durumunu ve **Mail’e aktar / Vazgeç** seçeneklerini
gösterir; üretilen metin akış sırasında salt okunur önizlemede görüntülenir. İçerik tamamen hazır olmadan aktarım
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

### TAB 3.2 tamamlanma sözleşmesi

TAB 3.2 istek alanları korunur. Önceki sürümle uyumlu olarak ardışık `data:`
satırları (boş SSE ayıracı olmasa da), NDJSON, tek/çok satırlı JSON belgeleri ve
ham metin desteklenir. `application/json` başlığıyla gelen satır akışları da
parçalar geldikçe işlenir. `[DONE]` veya `done: true` varsa tamamlanma için
kullanılır; bu işaretleri göndermeyen eski servislerde normal HTTP akış sonu
kabul edilir. Bu eski sözleşmede, sağlayıcının içerik üretimini erken bitirip
HTTP yanıtını normal kapatması istemci tarafından kesin olarak ayırt edilemez;
aktarımdan önce önizlemeyi kontrol edin.

Açık `done: false` ile biten yanıt, eksik/bozuk JSON, geçersiz UTF-8, hata olayı,
HTTP/okuma hatası, iptal ve `stop` dışındaki başarısız bitiş nedenleri reddedilir.
`response`, `text`, `content`, `output`, `message.content` ve OpenAI `choices`
alanları desteklenir. PLUS AI servisinin SSE ve `[DONE]` sözleşmesi değişmez.
Her iki sağlayıcıda istek/yanıt sınırı 1 MiB; toplam süre `TimeoutMinutes` kadardır.

### Yeni doğrulama kapsamı

`dotnet run --project tests/TransportTests.csproj` artık mail onay durumlarını,
paragraf sınırlarını ve TAB 3.2 akışlarını da kontrol eder. Ayrıca gerçek aktarım
sınıfını test nesneleriyle çalıştırarak hedef/aralık seçimini, değişmiş hedefin
reddedilmesini ve yeni taslak hata temizliğini sınar. Ağ veya Outlook gerektirmez.
Bu simülasyonlar gerçek COM ömrünü, Word biçimlendirmesini, Outlook otomatik
kaydetmesini veya Windows arayüzünü doğrulamaz. Test projesindeki CA1416 istisnası
yalnızca bağlı Windows adaptörünün gerçek COM nesnesi kullanılmayan bu çalıştırıcısı içindir.

Windows üzerinde Visual Studio'nun **Office/SharePoint development** araçlarıyla
`Askai.sln` çözümünü derleyip klasik Outlook'ta şu kontrolleri yapın:

1. Ana pencere, mail okuma ve taslak pencerelerinde uygun yeteneklerin görünmesi;
   Ayarlar penceresinin ve PLUS AI/TAB 3.2 seçiminin çalışması.
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
   önizlemesinin üretim sırasında güncellenmesi ve normal Outlook gönderiminin kullanıcı kontrolünde olması.

### Görünen adlar ve eklenti kimliği

Eklenti Outlook ve kurulum ekranlarında **AI Asistan** adıyla görünür. Servis
seçenekleri **PLUS AI** ve **TAB 3.2** olarak gösterilir; hata mesajları da bu
adları kullanır. **Ayarlar**, her pencerede **AI Asistan** grubunda bulunur.
**Ayarlar → Mail biçimlendirme → Şerit düzeni** iki seçenek sunar:

- **1 — Ayrı yanıt düğmeleri:** Ana şeritte Mail Hazırla büyük; Yanıtla,
  Tümünü Yanıtla ve Ayarlar küçüktür. Varsayılan ve eski ayarların düzenidir.
- **2 — Yanıt menüsü, büyük düğmeler:** Ana şeritte Mail Hazırla, oklu
  Yanıtla menüsü ve Ayarlar büyük görünür. Menüde Yanıtla/Tümünü Yanıtla vardır.

Seçim `RibbonPreset` alanında saklanır. Kaydet açık pencerelerin şeritlerini
günceller; Vazgeç uygulamaz. Okuma penceresi yanıt yeteneklerini, taslak
penceresi Metni İyileştir yeteneğini korur; ikisinde de Ayarlar aynı gruptadır.

Ayarlar penceresinin **Servis** sekmesinde servis, PLUS AI modeli ve token;
**Gelişmiş** sekmesinde TLS ve seçili servisin sertifika ayarları bulunur.
**Kaydet** ve **Vazgeç** düğmeleri kaydırılan alanın dışında kalır. TAB 3.2
seçildiğinde PLUS AI erişim bölümü gizlenir; mevcut model/token değerleri
korunur. Pencere varsayılan 520×480 boyutundadır; küçük yüksekliklerde sekme
içeriği kaydırılır ve Kaydet/Vazgeç düğmeleri erişilebilir kalır. Enter kaydeder, Escape değişiklikleri kaydetmeden kapatır.

Mevcut kurulum ve ayarların uyumluluğu için assembly/namespace, proje dosya
adı, sağlayıcı enum değerleri (`Central`/`Legacy`), `AppConfig` alanları ve
`%LOCALAPPDATA%\Askai\settings.xml` içindeki anahtarlar değiştirilmez. Bunlar
görünen marka adları değildir; önceki kurulumların token ve sertifikaları
yeniden kullanılabilir.

`Assets/Assistant.svg` ölçeklenebilir ikon kaynağıdır. Aynı ikon PNG ve
16–256 piksel boyutlarını içeren ICO olarak sağlanır; eklenti assembly'sine
gömülür, Ayarlar düğmesinde ve pencerelerin başlık çubuğunda kullanılır.
İkonlar ek bir çalışma zamanı bağımlılığı gerektirmez.

## Markdown ve mail biçimlendirme

**Ayarlar → Mail biçimlendirme → Markdown uygula** seçeneği ilk kurulumda
kapalıdır. Eski ayar dosyalarında da kapalı kabul edilir. Seçim XML içindeki
`ApplyMarkdown` alanında saklanır ve her AI işleminin başında alınır;
PLUS AI ve TAB 3.2 için aynı şekilde çalışır.

Açıkken AI’dan Markdown mail gövdesi istenir. Önizleme ve Outlook aktarımı
ortak dönüştürücü kullanır: başlıklar, kalın/italik/üstü çizili yazı, iç içe
numaralı ve madde listeleri, alıntılar, bağlantılar, kod ve pipe tablolar
desteklenir. Normal satır sonları da korunur. Ham HTML metin olarak kalır;
görseller yüklenmeden alternatif metinleri gösterilir. Yalnız HTTP, HTTPS
ve mailto bağlantıları etkin bağlantı olarak aktarılır.

Yeni Markdown mailleri HTML oluşturulur. Mevcut düz metin mailin biçimi
değiştirilmez; önizlemede bu sınırlama belirtilir ve Markdown işaretleri
çıkarılmış okunabilir metin aktarılır. Tablolar bu durumda sekmeyle ayrılmış
sütunlar ve ayrı satırlar halinde korunur. HTML/RTF maillerde üretilen RTF,
Word editörünün yalnız hedef aralığına dosyadan eklenir. Pano kullanılmaz;
geçici RTF aktarım sonunda silinir. İmza ve önceki yazışmalar korunur,
uygulama mail göndermez.

Markdig **1.4.0**, .NET Framework 4.7.2 ile uyumlu Markdown ayrıştırıcısıdır
(BSD-2-Clause: https://github.com/xoofx/markdig/blob/main/license.txt).
İç içe işaretleme, kaçışlar ve tabloları güvenilir ayrıştırmak için eklenmiştir;
standart kütüphanede Markdown ayrıştırıcısı bulunmaz. Elle regex yaklaşımı
bu kapsamda uyum ve bakım riskini artırır. Paket ve framework bağımlılıkları
NuGet restore ile gelir; kurulum çıktısına ek assembly’ler dahil olur.
Yeni bir ağ servisi eklenmez. Paket sürümü sabittir;
güncellemeler mevcut dönüşüm testleriyle doğrulanmalıdır.

Sertifika kontrollerinden bağımsız ilgili testler:

```sh
dotnet run --project tests/TransportTests.csproj -- --mail-only
```

Windows’ta ayrıca Markdown açık/kapalı olarak yeni mail, seçili metni
İyileştir, Yanıtla ve Tümünü Yanıtla işlemlerini HTML/RTF/düz metin hedeflerde
kontrol edin. İmza ve seçim dışındaki biçimleri, paragraf ortası seçimleri,
iç içe listelerin sırasını, tablo hücrelerini ve uzun akış sırasında
önizlemeyi kaydırmayı doğrulayın. RTF import ve gerçek Outlook görünümü
simülasyon testleriyle kanıtlanamaz.

## Kişisel hafıza

**Ayarlar → Hafıza** sekmesinde rolünüzü, üslubunuzu ve yazım tercihlerinizi
tek bir metin alanında tanımlayın. Örneğin: “Proje koordinatörüyüm. Kısa ve
nazik yaz. Teknik terimleri sade bir dille açıkla.” Sınır 4.000 karakterdir;
uzun metin sessizce kırpılmaz, kaydetmeden önce kısaltmanız istenir.

**Hafızayı kullan** varsayılan açıktır. Boş hafıza üretimi etkilemez. Kapatmak
metni korur; **Temizle** alanı boşaltır. Bu değişiklikler yalnızca **Kaydet** ile
kalıcılaşır; Vazgeç veya pencereyi kapatmak kaydetmez. Sonraki mail işleminde
kaydedilmiş bilgiler kullanılır; Outlook'u yeniden başlatmanız gerekmez.
Devam eden üretim başlangıçta yüklediği ayarları kullanır.

Hafıza yeni mail, yanıt, tümünü yanıtla ve seçili metni iyileştirmede PLUS AI
ve TAB 3.2'ye kişisel bağlam olarak iletilir. Etkinken metnin tamamı her
üretimde seçili AI servisine gönderilir. Mevcut sistem/görev kuralları korunur;
işleme özel talimatlar hafızadaki genel tercihlerden önceliklidir. Hafıza imza,
gönderen kimliği, bilinmeyen olgu veya taahhüt üretme yetkisi vermez.

Metin mevcut `settings.xml` içinde Windows DPAPI CurrentUser ile şifrelenir.
Aynı Windows kullanıcısının tüm Outlook hesaplarında geçerlidir; cihazlar
arasında eşitlenmez ve maillerden otomatik öğrenme yapılmaz. Eski ayar
kayıtları boş hafızayla yüklenir. Şifreli kayıt okunamazsa mevcut güvenli ayar
hata mesajı gösterilir; ham hafıza hata mesajına veya loga yazılmaz.

## Kaynak sınırları ve arayüz yanıt süresi

İçerik boyutları sessizce kırpılmaz. Desteklenen bir sınır aşılırsa işlem
maili değiştirmeden açıklayıcı hata verir. Biçimlendirme bütçesi aşılırsa
yanıtın tamamı, Markdown işaretleri korunarak düz metin biçiminde gösterilir
ve aktarılır; bu durum onay ekranında belirtilir.

| Alan | Sınır / davranış |
| --- | --- |
| AI'a gönderilen kaynak | Gönderen bilgisi dahil 32.000 karakter |
| AI tarafından üretilen içerik | 64.000 karakter; ağ cevabı ayrıca en fazla 1 MiB |
| Markdown tablo | En fazla 12 sütun, başlık dahil 200 satır; mailde toplam 2.000 hücre |
| Üretilen RTF | En fazla 1.048.576 karakter; aşımda tüm içerik düz metne döner |
| Canlı biçimli önizleme | 12.000 kaynak ve 128.000 RTF karakterine kadar |
| Büyük canlı önizleme | Metin artımlı gösterilir; tamamlanınca son biçim arka planda hazırlanır |
| İyileştirme yapılan Word belgesi | En fazla 128.000 karakter |
| Kaynak mail | Ekler dahil toplam 50 MiB; HTML gövdesi en fazla 1.048.576 karakter, RTF gövdesi 4 MiB |
| Düzenleme öncesi Word seçim paketi | En fazla 4.194.304 XML karakteri |
| Kaydedilmiş ayar XML'i | En fazla 4.194.304 karakter; DTD ve dış XML kaynakları kabul edilmez |

Boyut ön kontrolü ve kaynak metin doğrulaması, zengin gövde snapshot'ından
önce yapılır. Outlook gövde özelliklerini tek parçada verir; gövdeye özel
sınır, bu COM okumasından sonra uygulanabilir. Toplam mail boyutu kontrolü
bu okuma için ön koruma sağlar. Değişiklik tespiti için HTML/RTF'nin ikinci
tam kopyası yerine SHA-256 özeti tutulur; Word metni ayrıca kontrol edilir.

Markdown ayrıştırma ve RTF üretimi arka planda yapılır. Aynı anda tek canlı
önizleme dönüşümü çalışır; eski metin veya pencere genişliği için hazırlanan
sonuçlar uygulanmaz. Onay düğmesi, son yanıtın önizlemesi tamamlanmadan
açılmaz. Son biçimlendirilmiş içerik Outlook aktarımında yeniden kullanılır.

Sertifika seçimi, kayıt doğrulaması ve istek öncesi kontrol UI dışında
çalışır. Kuyrukta bekleme dahil kontrol için 30 saniye sınırı vardır; kullanıcı
iptali veya pencerenin kapanması beklemeyi bitirir. Native zincir oluşturucu
zorla durdurulmaz. Tamamlanana kadar yalnızca bir native kontrol çalıştırılır;
sonraki işler iptal edilebilir kuyrukta bekler. İstek öncesi sertifika süresi,
genel AI istek zaman aşımına dahildir. Doğrulanmış değişmez zincir yapısı için
16 kayıtlık önbellek kullanılır; sertifika tarihleri her kullanımda, gerçek
sunucu ve iptal durumu ise seçili modun TLS politikasıyla kontrol edilir.

Şerit oluşturulurken yalnız görünüm tercihi okunur; token ve hafıza çözülmez.
Tercih süreç içinde önbelleğe alınır ve başarılı ayar kaydında güncellenir.

Mevcut taslağı iyileştirme işlemi bir Word özel geri alma kaydında tutulur.
Kısmi yazma veya paragraf biçimi hatasında canlı Word metni ve seçim XML'i
karşılaştırılır; yalnız bu düzenleme değişiklik yaptıysa geri alınır. Önceden
yapılmış kullanıcı düzenlemesi geri alınmaz. Geri alma da başarısız olursa
kısmi değişiklik olabileceği açıkça bildirilir. Geçici RTF temizleme hatası,
başarılı aktarımı başarısız sayıp yeni taslağı silmez; içerik/yol loglanmaz.
Word geri alma API'sinin çalışma biçimi için
[Microsoft UndoRecord belgesi](https://learn.microsoft.com/en-us/office/vba/word/concepts/working-with-word/working-with-the-undorecord-object)
temel alınmıştır; gerçek Outlook davranışı Windows üzerinde doğrulanmalıdır.
