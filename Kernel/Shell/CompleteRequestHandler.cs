

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

        private async Task<CompletionList> ShowCompletionAsync(int pos, char? triggerChar)
        {
            completionCancellation = new CancellationTokenSource();
            var cancellationToken = completionCancellation.Token;

        //    var completionList = new int pos
            

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

                /*  foreach (var completionItem in completionList.Items)
                  {
                      completionWindow.CompletionList.CompletionData.Add(new CodeCompletionData(completionItem.DisplayText,
                          () => GetDescriptionAsync(completionService, document, completionItem), completionItem.Tags));
                  }*/

                /*   if (triggerChar == null || IsAllowedLanguageLetter(triggerChar.Value))
                {
                    completionWindow.StartOffset = word.Item1;
                    completionWindow.CompletionList.SelectItem(word.Item2);
                }*/
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

            //   this.logger.Debug("Position: " + completeRequest.CursorPosition);

            this.logger.Debug("Document: " + documentFile.GetTextAsync().Result);
            this.logger.Debug("Doucment Length: " + documentFile.GetTextAsync().Result.Length);

            var _list = ShowCompletionAsync(completeRequest.CursorPosition, '.').Result;

           // CustomCompletionProvider customCompletionProvider = new CustomCompletionProvider();
           // var completionTriggers = CompletionTrigger.CreateInsertionTrigger('.');

            //    var cancellationToken = new CancellationToken();


            //   CompletionContext completionContext = new CompletionContext(customCompletionProvider, documentFile, completeRequest.CursorPosition, new TextSpan(), CompletionTrigger.Invoke, documentFile.Project.Solution.Workspace.Options, cancellationToken);
            //    customCompletionProvider.ProvideCompletionsAsync(completionContext).Wait();

            // var _list = GetCompletions(completionContext);//customCompletionProvider.ProvideCompletionsAsync(documentFile, completeRequest.CursorPosition, cancellationToken);

            //     var dot = CompletionTrigger.CreateInsertionTrigger('.');
            //      var should_trigger = customCompletionProvider.ShouldTriggerCompletion(documentFile.GetTextAsync().Result, completeRequest.CursorPosition, dot, null);

            //     this.logger.Debug("Custom Should trigger: " + should_trigger);

            //completions.
            //        var completionService = CompletionService.GetService(documentFile);

            //          should_trigger = completionService.ShouldTriggerCompletion(documentFile.GetTextAsync().Result, completeRequest.CursorPosition, dot);

            //         this.logger.Debug("Normal Should trigger: " + should_trigger);

            /*     if (should_trigger)
                 {
                     var normal_list = completionService.GetCompletionsAsync(documentFile, completeRequest.CursorPosition, dot);
                     if (normal_list.Result.Items.Length > 0)
                     {
                         foreach (var item in normal_list.Result.Items)
                         {
                             this.logger.Debug("Normal: " + item.DisplayText);
                         }
                     }
                 }*/

            List<CompleteReplyMatch> matches_ = new List<CompleteReplyMatch>();

            /*   foreach (var completionItem in _list)
                {
                    CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch()
                    {
                        Name = completionItem.DisplayText
                    };
                    matches_.Add(completeReplyMatch);
                }*/

            if (_list != null)
            {
                if (_list.Items.Length > 0)
                {

                    foreach (var completionItem in _list.Items)
                    {
                        CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch()
                        {
                            Name = completionItem.DisplayText
                        };
                        matches_.Add(completeReplyMatch);
                    }
                }
                else
                {
                    this.logger.Debug("List was empty");
                }
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

      /*  private async Task<List<CompleteReplyMatch>> ShowCompletionAsync(int pos)
        {
            var completionService = CompletionService.GetService(documentFile);
            var cancellationToken = new CancellationToken();

            var completionList = await completionService.GetCompletionsAsync(documentFile, pos, cancellationToken: cancellationToken);
            List<CompleteReplyMatch> matches_ = new List<CompleteReplyMatch>();


            if (completionList == null)
            {
                return matches_;
            }


            foreach (var completionItem in completionList.Items)
            {
                CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch()
                {
                    Name = completionItem.DisplayText
                };
                matches_.Add(completeReplyMatch);
            }

            return matches_;
        }

        public string FindWordToAutoComplete(string line)
        {
            line = Regex.Replace(line, @"[^\w&^\.]", "*");

            string cursorWord, cursorLine;

            Regex p = new Regex(@".*\*"); //regex to match up to last '*'
            Match mat = p.Match(line);

            if (mat.Success)
            {

                cursorLine = line.Substring(mat.Index + mat.Length);

            }
            else
            {
                cursorLine = line;
            }


            p = new Regex(@".*\.");
            mat = p.Match(cursorLine);

            if (mat.Success)
            {
                cursorWord = cursorLine.Substring(mat.Index + mat.Length);


            }
            else
            {
                cursorWord = cursorLine;
                cursorLine = "";
            }

            return cursorWord;

        }*/

    }
}
