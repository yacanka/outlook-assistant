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
yazılmaz. İç exception tanı için korunur; sertifika doğrulaması ve TLS ayarları
değiştirilmez.

Curl ile yapılan GET isteğinde 200 alınması, uygulamanın token, model ve
`stream: true` içeren POST çağrısının başarılı olacağını kanıtlamaz. Testler HTTP,
yanıt türü ve bağlantı hatalarının güvenli sınıflandırmasını ağ kullanmadan kontrol
eder; gerçek Windows TLS ortamını taklit etmez.

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
