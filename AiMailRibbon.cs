


using Ask.ai;
using Askai;
using Microsoft.Office.Tools.Ribbon;
using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Ask.ai
{
    public partial class AiMailRibbon
    {
        // ═══════════════════════════════════════════════════════
        //  RIBBON OLAYLARI
        // ═══════════════════════════════════════════════════════

        private void AiMailRibbon_Load(object sender, RibbonUIEventArgs e)
        {
            // Ribbon yüklendiğinde yapılacak işlemler
        }

        /// <summary>
        /// "AI ile Yanıtla" butonuna tıklandığında çalışır.
        /// </summary>
        private async void btnAiReply_Click(object sender, RibbonControlEventArgs e)
        {
            await ProcessAiReplyAsync(replyAll: false);
        }

        /// <summary>
        /// "AI ile Tümünü Yanıtla" butonuna tıklandığında çalışır.
        /// </summary>
        private async void btnAiReplyAll_Click(object sender, RibbonControlEventArgs e)
        {
            await ProcessAiReplyAsync(replyAll: true);
        }

        // ═══════════════════════════════════════════════════════
        //  ANA İŞLEM AKIŞI
        // ═══════════════════════════════════════════════════════

        private async Task ProcessAiReplyAsync(bool replyAll)
        {
            // ── Butonları devre dışı bırak ──
            SetButtonsEnabled(false);

            try
            {
                // ── 1. Seçili maili al ──
                var mailItem = GetSelectedMailItem();
                if (mailItem == null) return;

                // ── 2. Mail içeriğinden prompt oluştur ──
                string prompt = BuildPrompt(mailItem);

                // ── 3. İlerleme formunu göster ──
                using (var cts = new CancellationTokenSource())
                {
                    var progressForm = new ProgressForm(cts);
                    progressForm.Show();

                    try
                    {
                        // ── 4. AI'ya gönder ve streaming yanıt al ──
                        var aiService = new AiService();

                        progressForm.SetConnected();

                        string aiResponse = await aiService.SendStreamingRequestAsync(
                            prompt,
                            chunk =>
                            {
                                // Her parça geldiğinde önizlemeyi güncelle
                                progressForm.AppendPreviewText(chunk);
                            },
                            cts.Token);

                        // ── 5. Yanıt tamamlandı ──
                        progressForm.SetCompleted();

                        // Kullanıcı tamamlandığını görsün
                        await Task.Delay(800);

                        // ── 6. Formu kapat ──
                        if (!progressForm.IsDisposed)
                            progressForm.Close();

                        // ── 7. Yanıt boş mu kontrol et ──
                        if (string.IsNullOrWhiteSpace(aiResponse))
                        {
                            MessageBox.Show(
                                "Yapay zeka boş bir yanıt döndürdü.",
                                "Uyarı",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            return;
                        }

                        // ── 8. Reply oluştur ve AI yanıtını yerleştir ──
                        CreateReplyWithAiResponse(mailItem, aiResponse, replyAll);
                    }
                    catch (OperationCanceledException)
                    {
                        // Kullanıcı iptal etti - sessizce çık
                    }
                    catch (Exception ex)
                    {
                        if (!progressForm.IsDisposed)
                            progressForm.Close();

                        MessageBox.Show(
                            $"AI yanıtı alınırken hata oluştu:\n\n{ex.Message}",
                            "Hata",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Beklenmeyen hata:\n\n{ex.Message}",
                    "Hata",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // ── Butonları tekrar etkinleştir ──
                SetButtonsEnabled(true);
            }
        }

        // ════════════════════════════════════════════════════���══
        //  YARDIMCI METOTLAR
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Outlook Explorer'dan seçili mail öğesini alır.
        /// </summary>
        private Outlook.MailItem GetSelectedMailItem()
        {
            var explorer = Globals.ThisAddIn.Application.ActiveExplorer();

            if (explorer == null || explorer.Selection == null || explorer.Selection.Count == 0)
            {
                MessageBox.Show(
                    "Lütfen önce bir e-posta seçin.",
                    "Mail Seçilmedi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return null;
            }

            var mailItem = explorer.Selection[1] as Outlook.MailItem;

            if (mailItem == null)
            {
                MessageBox.Show(
                    "Seçili öğe bir e-posta değil.\nLütfen bir e-posta seçin.",
                    "Geçersiz Seçim",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            return mailItem;
        }

        /// <summary>
        /// Mail içeriğinden AI'ya gönderilecek promptu oluşturur.
        /// </summary>
        private string BuildPrompt(Outlook.MailItem mailItem)
        {
            string senderName = "Bilinmiyor";
            string senderEmail = "";

            try
            {
                senderName = mailItem.SenderName ?? "Bilinmiyor";
                senderEmail = mailItem.SenderEmailAddress ?? "";
            }
            catch { /* COM erişim hatası olabilir */ }

            string receivedDate = "";
            try
            {
                receivedDate = mailItem.ReceivedTime.ToString("dd.MM.yyyy HH:mm");
            }
            catch { }

            string body = "";
            try
            {
                body = mailItem.Body ?? "";
                // Çok uzun mailleri kısalt (token limiti için)
                if (body.Length > 8000)
                    body = body.Substring(0, 8000) + "\n\n[... mail içeriği kısaltıldı ...]";
            }
            catch { }

            return
                $"{AppConfig.SystemPrompt} Aşağıdaki e-postaya profesyonel bir yanıt yaz. Sadece metin gövdesini yaz. Yalnızca cevap istiyorum. Benimle diyalog kurma:\n\n" +
                $"═══════════════════════════════════\n" +
                $"Gönderen : {senderName} <{senderEmail}>\n" +
                $"Konu     : {mailItem.Subject ?? "(Konu yok)"}\n" +
                $"Tarih    : {receivedDate}\n" +
                $"═══════════════════════════════════\n\n" +
                $"{body}";
        }

        /// <summary>
        /// Reply/ReplyAll oluşturur ve AI yanıtını HTML olarak yerleştirir.
        /// </summary>
        private void CreateReplyWithAiResponse(
            Outlook.MailItem originalMail,
            string aiResponse,
            bool replyAll)
        {
            // Reply veya ReplyAll oluştur
            Outlook.MailItem reply = replyAll
                ? originalMail.ReplyAll()
                : originalMail.Reply();

            // AI yanıtını HTML'e çevir
            string aiHtml = PlainTextToHtml(aiResponse);

            // Mevcut reply HTML'ine AI yanıtını yerleştir
            InsertContentIntoHtmlBody(reply, aiHtml);

            // Reply penceresini aç (kullanıcı göndermeden önce düzenleyebilir)
            reply.Display(false);
        }

        /// <summary>
        /// Düz metni HTML formatına çevirir.
        /// </summary>
        private string PlainTextToHtml(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "<p></p>";

            // HTML özel karakterlerini escape et
            string html = WebUtility.HtmlEncode(plainText);

            // ── Tüm newline varyasyonlarını önce tek formata normalize et ──
            html = html.Replace("\r\n", "\n");
            html = html.Replace("\r", "\n");

            // ── Ardışık boş satırları paragraf ayrımına çevir ──
            // \n\n → paragraf sonu (daha fazla boşluk)
            html = Regex.Replace(html, @"\n{3,}", "\n\n"); // 3+ boş satırı 2'ye indir
            html = html.Replace("\n\n", "</p><p>");       // çift newline → yeni paragraf

            // ── Tek satır sonlarını <br> yap ──
            html = html.Replace("\n", "<br>\n");

            // ── Boşlukları koru ──
            html = html.Replace("  ", "&nbsp;&nbsp;");
            html = html.Replace("\t", "&nbsp;&nbsp;&nbsp;&nbsp;");

            // ── Stil ile sar ──
            return
                "<div style=\"" +
                "font-family: Calibri, 'Segoe UI', Arial, sans-serif; " +
                "font-size: 11pt; " +
                "color: #1F1F1F; " +
                "line-height: 1.6; " +
                "margin-bottom: 16px;" +
                "\">\n" +
                "<p>" + html + "</p>\n" +
                "</div>";
        }

        /// <summary>
        /// Reply HTML'inin body etiketinden sonra AI yanıtını ekler.
        /// Orijinal mail alıntısı korunur.
        /// </summary>
        private void InsertContentIntoHtmlBody(Outlook.MailItem reply, string contentHtml)
        {
            string existingHtml = reply.HTMLBody ?? "";

            // <body> etiketini bul (attributes olabilir: <body lang="TR">)
            var bodyMatch = Regex.Match(
                existingHtml,
                @"<body[^>]*>",
                RegexOptions.IgnoreCase);

            if (bodyMatch.Success)
            {
                // <body> etiketinden hemen sonra AI yanıtını ekle
                int insertPosition = bodyMatch.Index + bodyMatch.Length;

                reply.HTMLBody = existingHtml.Insert(
                    insertPosition,
                    "\n" + contentHtml + "<br>\n");
            }
            else
            {
                // body etiketi bulunamazsa başa ekle
                reply.HTMLBody = contentHtml + "<br>\n" + existingHtml;
            }
        }

        /// <summary>
        /// Ribbon butonlarının aktiflik durumunu ayarlar.
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            btnAiReply.Enabled = enabled;
            btnAiReplyAll.Enabled = enabled;
        }
    }
}






