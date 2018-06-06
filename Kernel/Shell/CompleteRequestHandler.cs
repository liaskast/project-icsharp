

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

        public void HandleMessage(Message message, RouterSocket serverSocket, PublisherSocket ioPub)
        {
            CompleteRequest completeRequest = JsonSerializer.Deserialize<CompleteRequest>(message.Content);

            string code = completeRequest.CodeCells[0];
          // code = code.Substring(1, code.Length - 2);
			string line = completeRequest.Line;

            this.logger.Debug("Document Before");
            this.logger.Debug(documentFile.GetTextAsync().Result);

            var sourceText = SourceText.From(code);
            documentFile = documentFile.WithText(sourceText);

            this.logger.Debug("Document After");
            this.logger.Debug(documentFile.GetTextAsync().Result);

            this.logger.Debug("Position: " + completeRequest.CursorPosition);

            this.logger.Debug("Position: " + completeRequest.CursorPosition);

            CustomCompletionProvider customCompletionProvider = new CustomCompletionProvider();
            var completionTriggers = CompletionTrigger.CreateInsertionTrigger('.');

            var cancellationToken = new CancellationToken();
            

           CompletionContext completionContext = new CompletionContext(customCompletionProvider, documentFile, completeRequest.CursorPosition, new TextSpan(), CompletionTrigger.Invoke, documentFile.Project.Solution.Workspace.Options, cancellationToken);
           customCompletionProvider.ProvideCompletionsAsync(completionContext).Wait();

            var _list = GetCompletions(completionContext);//customCompletionProvider.ProvideCompletionsAsync(documentFile, completeRequest.CursorPosition, cancellationToken);

            var dot = CompletionTrigger.CreateInsertionTrigger('.');
            var should_trigger = customCompletionProvider.ShouldTriggerCompletion(documentFile.GetTextAsync().Result, completeRequest.CursorPosition, dot, null);
        
            this.logger.Debug("Custom Should trigger: " + should_trigger);

            //completions.
            var completionService = CompletionService.GetService(documentFile);

            should_trigger = completionService.ShouldTriggerCompletion(documentFile.GetTextAsync().Result, completeRequest.CursorPosition, dot);

            this.logger.Debug("Normal Should trigger: " + should_trigger);
            
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

            foreach (var completionItem in _list)
            {
                CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch()
                {
                    Name = completionItem.DisplayText
                };
                matches_.Add(completeReplyMatch);
            }


            // this.logger.Debug(ShouldTriggerCompletion(documentFile.GetTextAsync().Result, completeRequest.CursorPosition));



            /*
            code = Regex.Replace(code.Substring(1, code.Length - 2), @"\\n", "*");
            line = line.Substring(1, line.Length - 2);

            int cur_pos = completeRequest.CursorPosition;

            this.logger.Info("cur_pos " + cur_pos);

            line = line.Substring(0, cur_pos); //get substring of code from start to cursor position

            string cursorWord = FindWordToAutoComplete(line);*/


            CompleteReply completeReply = new CompleteReply()
            {
                //CursorEnd = 10,
                Matches = matches_, //ShowCompletionAsync(completeRequest.CursorPosition).Result,
                Status = "ok",
                //CursorStart = 5,
                // MetaData = null
            };

            Message completeReplyMessage = MessageBuilder.CreateMessage(MessageTypeValues.CompleteReply, JsonSerializer.Serialize(completeReply), message.Header);
            this.logger.Info("Sending complete_reply");
            this.messageSender.Send(completeReplyMessage, serverSocket);

        }

        private async Task<List<CompleteReplyMatch>> ShowCompletionAsync(int pos)
        {
            var completionService = CompletionService.GetService(documentFile);
            var cancellationToken = new CancellationToken();
            List<CompleteReplyMatch> matches_ = new List<CompleteReplyMatch>();

           // if (!(documentFile.GetTextAsync().Result.Length > 0))
            //{
             //   return matches_;
           // }
             
            var dot = CompletionTrigger.CreateInsertionTrigger('.');
            var should_trigger = completionService.ShouldTriggerCompletion(documentFile.GetTextAsync().Result, pos, dot);

            this.logger.Debug("Should trigger: " + should_trigger);

            var completionList = await completionService.GetCompletionsAsync(documentFile, pos+1, cancellationToken: cancellationToken);


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

        public static IReadOnlyList<CompletionItem> GetCompletions(CompletionContext context)
        {
            var property = context.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.NonPublic);
            return (IReadOnlyList<CompletionItem>)property.GetValue(context);
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

        }

    }
}
