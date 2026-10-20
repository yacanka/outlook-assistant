# Popup yerleşim testleri

Windows üzerinde .NET Framework 4.7.2 Developer Pack ve Visual Studio MSBuild ile çalıştırın:

```powershell
msbuild tests/PopupLayout/PopupLayoutTests.csproj /t:Rebuild /verbosity:minimal
& tests/PopupLayout/bin/PopupLayoutTests.exe
```

Testler gerçek WinForms kontrollerini kullanır; Outlook veya VSTO gerektirmez. Mail giriş ve onay pencerelerini varsayılan, dar ve geniş boyutlarda, tekrar daraltıldığında ve %150 ölçekle kontrol eder. Boş konu doğrulaması, arka plan parçalarının canlı önizlemeye aktarılması, son parçanın hazır durumunda görünmesi, kısmi yanıtın onaylanamaması, hazır durumuna geçiş, gerçek ayarlar penceresinin sekmeleri, servis adları, servis değişiminde PLUS AI alanlarının gizlenmesi ve token’ın korunması, Kaydet/Vazgeç düğmeleri, iki sütunlu yerleşim, uzun kesintisiz metin ve açıklamanın değişmesi de kapsanır.

macOS Mono'nun Carbon WinForms sürücüsü bu testleri çalıştırmak için uygun değildir. Derleme kontrolü görsel doğrulamanın yerine geçmez. Windows'ta ayrıca Outlook içinden üç popup'ı %100, %150 ve %200 ekran ölçeklerinde açın; pencereyi daraltıp genişletin, açıklamalar ile düğmelerin kırpılmadığını ve küçük yükseklikte dikey kaydırmayla erişilebilir olduğunu kontrol edin.

“Geri” regresyonları düğmenin yalnız önizleme hazırken etkinleşmesini,
geri/iptal/kapatmanın aktarımı onaylamamasını, aktarım başladıktan sonra geri
dönülememesini ve yeniden açılan giriş penceresinde konu ile çok satırlı
talimatın korunup düzenlenebilmesini kapsar. Windows/Outlook'ta Mail Hazırla,
Yanıtla ve Tümünü Yanıtla için art arda Geri → promptu düzenle → Hazırla
adımlarını deneyin; yalnız son onaylanan yanıtın bir kez aktarıldığını kontrol
edin. Prompt ekranı olmayan Seçili Metni İyileştir akışında Geri gösterilmez.

Ayarlar penceresi de bu projede gerçek üretim sınıfıyla derlenir. GUI açmadan
üç sertifika modunun veri seçimini, paket temizlemenin diğer modları
etkilemediğini ve gömülü ikonun pencere/şerit boyutlarında yüklenmesini
kontrol etmek için derlemeden sonra:

```sh
mono tests/PopupLayout/bin/PopupLayoutTests.exe --settings-only
```

Bu kontrol ayar dosyasına yazmaz. Windows üzerinde yeni paket modunu seçme,
zincir yükleme, Kaydet ve yeniden açma adımlarını ayrıca doğrulayın.

Markdown değişikliğinden sonra test projesi NuGet restore gerektirir:

```powershell
msbuild tests/PopupLayout/PopupLayoutTests.csproj /t:Restore,Rebuild /verbosity:minimal
```

Önizleme artık salt okunur RichTextBox kullanır. Testler canlı ve tamamlanmış
Markdown biçimlerini, satır sonlarını, düz metin hedef önizlemesini ve yeni
Görünüm sekmesini, şerit düzeni ve Markdown alanlarının başlıklarla ayrılmasını,
numarasız preset metinlerini ve açıklayıcı tooltip'leri de kapsar. `--settings-only` geçici, sentetik bir
ayar dosyasında Markdown seçiminin kaydedilip yeniden yüklenmesini doğrular;
kullanıcının ayar dosyasına yazmaz. Gerçek RichEdit görünümü ve Outlook RTF
aktarımının Windows üzerinde ayrıca kontrol edilmesi gerekir.

macOS üzerinde, Windows doğrulamasının yerine geçmeyen bağımsız bir RTF
okuyucu kontrolü de çalıştırılabilir. Fixture’lar yalnız sentetik içeriktir:

```sh
dotnet run --project tests/TransportTests.csproj -- --rtf-fixtures /tmp/askai-rtf-fixtures
python3 tests/verify_rtf_fixtures.py /tmp/askai-rtf-fixtures
```

Bu kontrol macOS `textutil` okuyucusundan gerçek liste, paragraf girintisi,
tablo hücresi, vurgu, Unicode, bağlantı ve kod çıktısını kontrol eder.
Windows’ta dar/geniş pencerelerde Markdown tablo önizlemesini ve akış
sırasında yukarı kaydırılan metnin konumunu ayrıca doğrulayın.

Hafıza testleri eski kayıtların boş hafızayla yüklenmesini, çok satırlı Unicode
metnin şifreli kaydetme/yükleme döngüsünü, kapatma/temizleme davranışını,
4.000 karakter sınırını ve bozuk kaydın güvenli hata mesajını kapsar.
`--settings-only` bu kontrolleri sentetik geçici dosyalarda çalıştırır.
Windows DPAPI davranışı ayrıca Windows üzerinde doğrulanmalıdır.
Arayüz kontrolleri Hafıza sekmesinin alanını, sayacını ve Temizle düğmesini
kapsar. Windows'ta Enter ile yeni satır, uzun metin yapıştırma, Kaydet/Vazgeç,
yeniden açma ve gerçek Outlook üretiminde kişisel üslup etkisini de doğrulayın.

Yeni issue regresyonları: 0/1/çok modelde seçici/etiket görünürlüğü, tek modelin
otomatik seçimi, Gelişmiş sekmesi, servise özel mod/sertifika taslaklarının
korunması ve temizlemenin diğer servisi etkilememesi Windows GUI testlerindedir.
Her iki servisi her sekmede dar/geniş boyutlarda ayrıca kontrol edin.

`--settings-only` eski kayıtların preset/sertifika varsayılanlarını, iki
servisin üç sertifika alanının kaydetme/yükleme döngüsünü ve gerçek üretim
ribbon sınıflarını Office sınır simülasyonuyla çalıştırır. Preset düğmeleri,
menü eylemleri/bağlamı, tüm pencere türleri, canlı güncelleme ve Close/Dispose
abonelik temizliği doğrulanır. Bu simülasyon gerçek VSTO derlemesini veya
Office XML önbelleğini doğrulamaz. Windows/Outlook'ta her iki preseti ana, okuma
ve taslak pencerelerinde, Kaydet/Vazgeç ve yeniden açma ile kontrol edin.

Gerçek `AiService` yönlendirmesi, her sağlayıcının üç modunda bozuk sertifika
verisinin ağ çağrısından önce reddedildiğini de doğrular. Sentetik geçici ayar
dosyaları kullanılır; testler kullanıcının kayıtlı ayarlarını değiştirmez.

## Güvenilirlik regresyonları

Tam platform bağımsız test paketi (native sertifika kontrolleri dahil):

```sh
dotnet run --project tests/TransportTests.csproj
```

Sertifika zinciri API'sine erişimin kısıtlandığı ortamlarda yalnız transport,
formatlama, güvenilirlik, TLS politikası ve Outlook simülasyonlarını çalıştırmak için:

```sh
dotnet run --project tests/TransportTests.csproj -- --transport-only
dotnet run --project tests/TransportTests.csproj -- --reliability-only
```

Bu seçenekler native sunucu/zincir testlerinin yerine geçmez; varsayılan
komut bu testleri çalıştırmaya devam eder. Regresyonlar seyrek/geniş tabloları,
satır ve toplam hücre bütçesini, RTF genişlemesini, 64.000 karakter sınırını,
UTF-8 emojinin 4.096 byte ağ sınırındaki tüm bölünmelerini, çok satırlı JSON
ve önizleme tamponunda doğrusal bellek tüketimini ve sertifika işinin arka
planda seri çalışmasını/iptalini kapsar. Kaynak boyutu ön kontrolü, kısmi
metin/biçim değişikliğinde geri alma ve yazma öncesi hatada eski kullanıcı
düzenlemesinin geri alınmaması Outlook simülasyonlarıyla doğrulanır.

Windows GUI testleri `SetReadyAsync` tamamlanmasını UI mesaj döngüsüyle bekler;
büyük canlı önizlemenin düz metne geçmesini, son Markdown biçiminin uygulanmasını
ve son dönüşüm sırasında kapatılan pencerenin onay verememesini de kontrol eder.
Windows/Outlook'ta ayrıca aynı metni koruyup yalnız biçimi değiştiren bir
iyileştirmeyi, Undo/Redo'yu, paragraf ortası seçimi, pencereyi kapatmayı ve
sertifika kontrolü sürerken servis değiştirmeyi deneyin. Simülasyonlar gerçek
COM `WordOpenXML` ve özel geri alma kaydının davranışını kanıtlamaz.
