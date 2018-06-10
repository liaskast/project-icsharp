

namespace iCSharp.Kernel.Shell
{
    using Common.Logging;
    using System.Collections.Generic;
    using Common.Serializer;
    using iCSharp.Messages;
    using NetMQ.Sockets;
    using iCSharp.Kernel.Helpers;
    using System.Text.RegularExpressions;
    using Microsoft.CodeAnalysis.Host;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Text;
    using Microsoft.CodeAnalysis.Completion;
    using System.Threading.Tasks;
    using System.Threading;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis.Options;
    using System.Reflection;

    public class CompleteRequestHandler : IShellMessageHandler
    {
        private ILog logger;
        private readonly IMessageSender messageSender;

        private volatile IWorkspaceService workspaceService;
        private volatile AdhocWorkspace workspace;
        private volatile Document documentFile;

        private CancellationTokenSource completionCancellation;


        public CompleteRequestHandler(ILog logger, IMessageSender messageSender)
        {
            this.logger = logger;
            this.messageSender = messageSender;

            workspace = new AdhocWorkspace();
            string projName = "Jupyter";
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, projName, projName, LanguageNames.CSharp);
            var newProject = workspace.AddProject(projectInfo);
            var sourceText = SourceText.From("");
            var newDocument = workspace.AddDocument(newProject.Id, "JupyterSource.cs", sourceText);
            documentFile = newDocument;

        }

        public static IReadOnlyList<CompletionItem> GetCompletions(CompletionContext context)
        {
            var property = context.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.NonPublic);
            return (IReadOnlyList<CompletionItem>)property.GetValue(context);
        }

        private async Task<CompletionList> ShowCompletionAsync(int pos, char? triggerChar)
        {
            completionCancellation = new CancellationTokenSource();
            var cancellationToken = completionCancellation.Token;


            if (triggerChar == null || triggerChar == '.') // || IsAllowedLanguageLetter(triggerChar.Value))
            {
                var position = pos;
                //  var word = GetWord(position);

                var document = documentFile;
                var completionService = CompletionService.GetService(document);

                this.logger.Debug("CompletionService Began");
                this.logger.Debug("Document: " + document.GetTextAsync().Result);
                this.logger.Debug("Doucment Length: " + document.GetTextAsync().Result.Length);
                this.logger.Debug("Position: " + position);

                var completionList = await Task.Run(async () =>
                        await completionService.GetCompletionsAsync(document, position, cancellationToken: cancellationToken), cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                return completionList;
            }
            return null;
        }


        public void HandleMessage(Message message, RouterSocket serverSocket, PublisherSocket ioPub)
        {
            CompleteRequest completeRequest = JsonSerializer.Deserialize<CompleteRequest>(message.Content);

            string code = completeRequest.CodeCells[0];
			string line = completeRequest.Line;
            var sourceText = SourceText.From(code);

            this.logger.Debug("SourceText: " + sourceText.ToString());

          //  code = /*Regex.Replace(*/code.Substring(1, code.Length - 2);/*, @"\n", " ");*/

            sourceText = SourceText.From(code);
            this.logger.Debug("SourceText: " + sourceText.ToString());

            documentFile = documentFile.WithText(sourceText);

            this.logger.Debug("Document: " + documentFile.GetTextAsync().Result);
            this.logger.Debug("Doucment Length: " + documentFile.GetTextAsync().Result.Length);

          //  var _list = ShowCompletionAsync(completeRequest.CursorPosition, '.').Result;

            //// Custom Attempt
            var cancellationToken = new CancellationToken();
            CustomCompletionProvider customCompletionProvider = new CustomCompletionProvider();
            CompletionContext completionContext = new CompletionContext(customCompletionProvider, documentFile, completeRequest.CursorPosition, new TextSpan(), CompletionTrigger.Invoke, documentFile.Project.Solution.Workspace.Options, cancellationToken);
            customCompletionProvider.ProvideCompletionsAsync(completionContext).Wait();

             var _list = GetCompletions(completionContext);//customCompletionProvider.ProvideCompletionsAsync(documentFile, completeRequest.CursorPosition, cancellationToken);
     
            List<CompleteReplyMatch> matches_ = new List<CompleteReplyMatch>();

            if (_list != null)
            {
         //       if (_list.Item.Length > 0)
          //      {

                    foreach (var completionItem in _list)
                    {
                        CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch()
                        {
                            Name = completionItem.DisplayText
                        };
                        matches_.Add(completeReplyMatch);
                    }
               // }
                //else
                //{
                 //   this.logger.Debug("List was empty");
                //}
            }
            else
            {
                this.logger.Debug("List is null");
            }


            // this.logger.Debug(ShouldTriggerCompletion(documentFile.GetTextAsync().Result, completeRequest.CursorPosition));


            CompleteReply completeReply = new CompleteReply()
            {
                //CursorEnd = 10,
                Matches = matches_, //ShowCompletionAsync(completeRequest.CursorPosition).Result,
             //   Matches = ShowCompletionAsync(2).Result,
                Status = "ok",
                //CursorStart = 5,
                // MetaData = null
            };

 

            Message completeReplyMessage = MessageBuilder.CreateMessage(MessageTypeValues.CompleteReply, JsonSerializer.Serialize(completeReply), message.Header);
            this.logger.Info("Sending complete_reply");
            this.messageSender.Send(completeReplyMessage, serverSocket);

        }


    }
}
