using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Askai
{
    // Only application-authored messages may use these types; UI must never display raw
    // provider bodies, redirect locations, URLs or transport exception messages.
    internal sealed class AiServiceException : HttpRequestException
    {
        public AiServiceException(string message, Exception cause = null) : base(message, cause) { }
    }

    internal sealed class AiResponseException : IOException
    {
        public AiResponseException(string provider) : base(provider +
            " AI yanıt akışı geçersiz veya tamamlanmadı. Servisin streaming desteğini ve akışın tamamlandığını kontrol edin.") { }
    }

    internal static class AiConnectionErrors
    {
        public static void CheckResponse(HttpResponseMessage response, string provider, bool requireEventStream)
        {
            int status = (int)response.StatusCode;
            string prefix = provider + " AI (HTTP " + status + "): ";
            if (!response.IsSuccessStatusCode)
                throw new AiServiceException(prefix + HttpAdvice(status));

            string mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                throw new AiServiceException(prefix +
                    "AI yanıtı yerine HTML sayfası (text/html) döndü. Kimlik doğrulaması, giriş sayfası veya proxy yanıtını kontrol edin.");

            // Keep compatibility with providers that omit the header but send valid SSE.
            if (requireEventStream && mediaType != null
                && !string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                string description = string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
                    ? "application/json" : "uyumsuz içerik türü";
                throw new AiServiceException(prefix + description +
                    " döndü; text/event-stream bekleniyor. Servisin streaming yanıtını desteklediğini kontrol edin.");
            }
        }

        private static string HttpAdvice(int status)
        {
            switch (status)
            {
                case 401: return "Kimlik doğrulaması reddedildi. AI Ayarları'ndaki token'ın geçerliliğini kontrol edin.";
                case 403: return "Erişim reddedildi. Hesabın servis ve model yetkilerini kontrol edin.";
                case 407: return "Proxy kimlik doğrulaması gerekiyor. Kurumsal proxy ayarlarını BT ile kontrol edin.";
                case 429: return "Servis kullanım sınırına ulaşıldı. Daha sonra yeniden deneyin.";
                default:
                    if (status >= 300 && status < 400)
                        return "Servis yönlendirme döndürdü; otomatik yönlendirme izlenmiyor. Giriş/proxy yönlendirmesini kontrol edin.";
                    return "Servis isteği başarısız. Bu HTTP koduyla servis yöneticisine başvurun.";
            }
        }

        public static AiServiceException FromTransport(string provider, HttpRequestException error)
        {
            bool tls = false;
            bool dns = false;
            for (Exception cause = error; cause != null; cause = cause.InnerException)
            {
                var web = cause as WebException;
                if (web != null)
                {
                    if (web.Status == WebExceptionStatus.TrustFailure)
                        return new AiServiceException(provider +
                            " AI: Windows sunucu sertifikasını doğrulayamadı. Sertifika zincirini, geçerlilik süresini ve adres eşleşmesini BT ile kontrol edin." + NativeErrorDetail(error), error);
                    tls |= web.Status == WebExceptionStatus.SecureChannelFailure;
                    dns |= web.Status == WebExceptionStatus.NameResolutionFailure
                        || web.Status == WebExceptionStatus.ProxyNameResolutionFailure;
                }
                tls |= cause is AuthenticationException;
                var socket = cause as SocketException;
                if (socket != null)
                    dns |= socket.SocketErrorCode == SocketError.HostNotFound
                        || socket.SocketErrorCode == SocketError.TryAgain || socket.SocketErrorCode == SocketError.NoData;
            }
            if (tls)
                return new AiServiceException(provider +
                    " AI: TLS güvenli bağlantısı kurulamadı. Windows ve sunucunun TLS ayarlarını, sertifika zincirini ve kurumsal proxy'yi kontrol edin." + NativeErrorDetail(error), error);
            if (dns)
                return new AiServiceException(provider +
                    " AI: DNS çözümlemesi başarısız. Servis/proxy adresini ve VPN bağlantısını kontrol edin.", error);
            return new AiServiceException(provider +
                " AI: Servise bağlantı kurulamadı. Servisin çalıştığını, ağ/VPN, proxy ve güvenlik duvarı erişimini kontrol edin.", error);
        }

        private static string NativeErrorDetail(Exception error)
        {
            for (Exception cause = error; cause != null; cause = cause.InnerException)
            {
                var native = cause as Win32Exception;
                if (native != null)
                    return " Windows hata kodu: 0x" + native.NativeErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ".";
            }
            return "";
        }
    }
}
