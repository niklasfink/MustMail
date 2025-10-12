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

        // Debug log for when this function is called
        Log.Debug("An email has been recived!");

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

        // Dbeug log for the raw message
        logger.Debug($"Raw message:\n{Encoding.UTF8.GetString(buffer.ToArray())}");

        // Set stream position back to 0
        stream.Position = 0;

        // Load the memory stream as a Mime Message
        MimeMessage? message = await MimeMessage.LoadAsync(stream, cancellationToken);

        // Debug log for the Mime Message
        logger.Debug($"Mime Message:\n {message.ToString()}");

        // If message is null then return an error
        if (message == null)
        {
            Log.Warning("Unable to read message as Mime Message!");
            return SmtpResponse.SyntaxError;
        }

        // Extract the sender address from the message
        string? senderAddress = (message.From.FirstOrDefault() as MimeKit.MailboxAddress)?.Address;

        if (string.IsNullOrEmpty(senderAddress))
        {
            logger.Warning("No sender address found in the message!");
            return SmtpResponse.MailboxUnavailable;
        }

        // Validate sender is in the allowed list
        if (!allowedSenders.Contains(senderAddress, StringComparer.OrdinalIgnoreCase))
        {
            logger.Warning("Sender address '{SenderAddress}' is not in the allowed senders list!", senderAddress);
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

        logger.Debug("Recipients list: {Recipients}", string.Join(", ", recipients.Select(r => r.EmailAddress.Address)));

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
            // Send email using the validated sender address
            await graphClient.Users[senderAddress].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.Warning($"Unknown Error:\n {ex.Message}");
            return SmtpResponse.SyntaxError;
        }


        // Log success message
        logger.Information("The email with the subject `{MessageSubject}` was received and sent to `{MessageTo}` as `{From}`!", message.Subject, message.To, senderAddress);

        // Return email received successfully
        return SmtpResponse.Ok;

    }
}