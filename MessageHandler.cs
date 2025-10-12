using System.Buffers;
using System.Text;
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

        // Create message 
        SendMailPostRequestBody requestBody = new()
        {
            Message = new Message
            {
                Subject = message.Subject,
                ToRecipients = recipients
            }

        };

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

        try
        {
            logger.Information("SMTP attempt: Sending '{Subject}' from {From} to {To}",
                message.Subject ?? "(no subject)", senderAddress, recipientList);

            // Send email using the validated sender address
            await graphClient.Users[senderAddress].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);

            // Record success
            HealthCheckService.Instance.RecordSuccess();
            logger.Information("SMTP success: '{Subject}' sent successfully", message.Subject ?? "(no subject)");

            // Return email received successfully
            return SmtpResponse.Ok;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            string errorMsg = $"Microsoft Graph error: {odataEx.Error?.Message ?? odataEx.Message}";
            logger.Error("SMTP failed: {ErrorMessage} | Subject: '{Subject}' | From: {From} | To: {To}",
                errorMsg, message.Subject ?? "(no subject)", senderAddress, recipientList);
            HealthCheckService.Instance.RecordFailure(errorMsg);
            return SmtpResponse.MailboxUnavailable;
        }
        catch (Exception ex)
        {
            string errorMsg = $"{ex.GetType().Name}: {ex.Message}";
            logger.Error("SMTP failed: {ErrorMessage} | Subject: '{Subject}' | From: {From} | To: {To}",
                errorMsg, message.Subject ?? "(no subject)", senderAddress, recipientList);
            HealthCheckService.Instance.RecordFailure(errorMsg);
            return SmtpResponse.TransactionFailed;
        }
    }
}