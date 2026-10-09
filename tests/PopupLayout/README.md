# Popup yerleşim testleri

Windows üzerinde .NET Framework 4.7.2 Developer Pack ve Visual Studio MSBuild ile çalıştırın:

```powershell
msbuild tests/PopupLayout/PopupLayoutTests.csproj /t:Rebuild /verbosity:minimal
& tests/PopupLayout/bin/PopupLayoutTests.exe
```

Testler gerçek WinForms kontrollerini kullanır; Outlook veya VSTO gerektirmez. Mail giriş ve onay pencerelerini varsayılan, dar ve geniş boyutlarda, tekrar daraltıldığında ve %150 ölçekle kontrol eder. Boş konu doğrulaması, hazır durumuna geçiş, iki sütunlu ayar yerleşimi, uzun kesintisiz metin ve açıklamanın değişmesi de kapsanır.

macOS Mono'nun Carbon WinForms sürücüsü bu testleri çalıştırmak için uygun değildir. Derleme kontrolü görsel doğrulamanın yerine geçmez. Windows'ta ayrıca Outlook içinden üç popup'ı %100, %150 ve %200 ekran ölçeklerinde açın; pencereyi daraltıp genişletin, açıklamalar ile düğmelerin kırpılmadığını ve küçük yükseklikte dikey kaydırmayla erişilebilir olduğunu kontrol edin.
