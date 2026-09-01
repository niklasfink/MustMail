using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using Serilog;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace MustMail;

public class MessageHandler(GraphServiceClient graphClient, ILogger logger, List<string> allowedSenders) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        // Create memory stream
        await using MemoryStream stream = new();

        // Get position 0 
        SequencePosition position = buffer.GetPosition(0);

        // Read buffer and write to memory stream
        while (buffer.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            await stream.WriteAsync(memory, cancellationToken);
        }

        // Get position 0 
        position = buffer.GetPosition(0);

        // Debug log for the raw message
        logger.Debug("Raw message:\n{RawMessage}", Encoding.UTF8.GetString(buffer.ToArray()));

        // Set stream position back to 0
        stream.Position = 0;

        // Load the memory stream as a Mime Message
        MimeMessage? message = await MimeMessage.LoadAsync(stream, cancellationToken);

        // Debug log for the Mime Message
        logger.Debug("Mime Message:\n {MimeMessage}", message?.ToString());

        // If message is null then return an error
        if (message == null)
        {
            logger.Warning("SMTP attempt failed: Unable to parse message as MIME");
            HealthCheckService.Instance.RecordFailure("Unable to parse message as MIME");
            return SmtpResponse.SyntaxError;
        }

        // Extract the sender address from the message
        string? senderAddress = (message.From.FirstOrDefault() as MimeKit.MailboxAddress)?.Address;

        if (string.IsNullOrEmpty(senderAddress))
        {
            logger.Warning("SMTP attempt failed: No sender address found");
            HealthCheckService.Instance.RecordFailure("No sender address found");
            return SmtpResponse.MailboxUnavailable;
        }

        // Validate sender is in the allowed list
        if (!allowedSenders.Contains(senderAddress, StringComparer.OrdinalIgnoreCase))
        {
            logger.Warning("SMTP attempt failed: Sender '{SenderAddress}' not in allowed list", senderAddress);
            HealthCheckService.Instance.RecordFailure($"Unauthorized sender: {senderAddress}");
            return SmtpResponse.MailboxUnavailable;
        }

        logger.Debug("Using sender address: {SenderAddress}", senderAddress);

        // Create list of recipients
        List<Recipient> recipients = message.To
        .OfType<MimeKit.MailboxAddress>() // only process mailbox addresses
        .Select(addr => new Recipient
        {
            EmailAddress = new EmailAddress
            {
                Address = addr.Address,      // plain email only
                Name = addr.Name             // optional, can be null or empty
            }
        }).ToList();

        string recipientList = string.Join(", ", recipients.Select(r => r.EmailAddress?.Address ?? "unknown"));
        logger.Debug("Recipients list: {Recipients}", recipientList);

        // Extract CC recipients
        List<Recipient> ccRecipients = message.Cc
            .OfType<MimeKit.MailboxAddress>()
            .Select(addr => new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = addr.Address,
                    Name = addr.Name
                }
            }).ToList();

        string? ccList = ccRecipients.Any() ? string.Join(", ", ccRecipients.Select(r => r.EmailAddress?.Address ?? "unknown")) : null;

        // Extract and process attachments
        List<Microsoft.Graph.Models.Attachment> attachments = new();
        List<string> attachmentNames = new();
        long attachmentBytesTotal = 0;

        foreach (var attachment in message.Attachments)
        {
            if (attachment is MimeKit.MimePart mimePart)
            {
                // Regular file attachment
                using var memoryStream = new MemoryStream();
                mimePart.Content.DecodeTo(memoryStream);
                byte[] attachmentBytes = memoryStream.ToArray();
                attachmentBytesTotal += attachmentBytes.Length;

                string fileName = mimePart.FileName ?? "unnamed-attachment";
                attachmentNames.Add(fileName);

                attachments.Add(new FileAttachment
                {
                    OdataType = "#microsoft.graph.fileAttachment",
                    Name = fileName,
                    ContentType = mimePart.ContentType.MimeType,
                    ContentBytes = attachmentBytes
                });

                logger.Debug("Processing attachment: {FileName}, Size: {Size} bytes, Type: {ContentType}",
                    fileName, attachmentBytes.Length, mimePart.ContentType.MimeType);
            }
            else if (attachment is MimeKit.MessagePart messagePart)
            {
                // Embedded email message
                if (messagePart.Message != null)
                {
                    string embeddedName = messagePart.Message.Subject ?? "embedded-message";
                    attachmentNames.Add(embeddedName);

                    using var memoryStream = new MemoryStream();
                    messagePart.Message.WriteTo(memoryStream);
                    byte[] messageBytes = memoryStream.ToArray();
                    attachmentBytesTotal += messageBytes.Length;

                    attachments.Add(new FileAttachment
                    {
                        OdataType = "#microsoft.graph.fileAttachment",
                        Name = embeddedName + ".eml",
                        ContentType = "message/rfc822",
                        ContentBytes = messageBytes
                    });

                    logger.Debug("Processing embedded message: {Name}, Size: {Size} bytes",
                        embeddedName, messageBytes.Length);
                }
            }
        }

        // Create message 
        SendMailPostRequestBody requestBody = new()
        {
            Message = new Message
            {
                Subject = message.Subject,
                ToRecipients = recipients
            }

        };

        // Add CC recipients only if there are any
        if (ccRecipients.Any())
        {
            requestBody.Message.CcRecipients = ccRecipients;
        }

        // Add attachments only if there are any
        if (attachments.Any())
        {
            requestBody.Message.Attachments = attachments;
        }

        // If message does contain a HTML body then use it
        if (message.HtmlBody != null)
        {
            requestBody.Message.Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = message.HtmlBody
            };
        }
        // Else use the text body instead
        else
        {
            requestBody.Message.Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = message.TextBody
            };
        }

        // Log email transfer information
        var emailInfo = new
        {
            Subject = message.Subject ?? "(no subject)",
            From = senderAddress,
            To = recipientList,
            Cc = ccList,
            AttachmentCount = attachmentNames.Count,
            Attachments = attachmentNames.Any() ? string.Join(", ", attachmentNames) : null,
            AttachmentBytes = attachmentBytesTotal,
            HasHtmlBody = message.HtmlBody != null,
            HasTextBody = message.TextBody != null
        };

        logger.Information("Email transfer initiated: {EmailInfo:l}",
            JsonSerializer.Serialize(emailInfo, new JsonSerializerOptions { WriteIndented = false }));

        try
        {

            // Send email using the validated sender address
            await graphClient.Users[senderAddress].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);

            // Record success
            HealthCheckService.Instance.RecordSuccess();
            logger.Information("Email sent successfully");

            // Return email received successfully
            return SmtpResponse.Ok;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            string graphCode = odataEx.Error?.Code ?? "unknown";
            string graphMessage = odataEx.Error?.Message ?? odataEx.Message;
            string errorMsg = $"Microsoft Graph error ({odataEx.ResponseStatusCode}, {graphCode}): {graphMessage}";
            logger.Error(odataEx, "Email send failed: {ErrorMessage}", errorMsg);
            HealthCheckService.Instance.RecordFailure(errorMsg);
            return SmtpResponse.TransactionFailed;
        }
        catch (Exception ex)
        {
            string errorMsg = $"{ex.GetType().Name}: {ex.Message}";
            logger.Error(ex, "Email send failed: {ErrorMessage}", errorMsg);
            HealthCheckService.Instance.RecordFailure(errorMsg);
            return SmtpResponse.TransactionFailed;
        }
    }
}