

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
			string line = completeRequest.Line;
            var sourceText = SourceText.From(code);
            this.logger.Debug(sourceText.ToString());

  



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
                Matches = ShowCompletionAsync(2).Result,
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

        private static async Task<ImmutableArray<TaggedText>> GetDescriptionAsync(CompletionService completionService, Document document, CompletionItem completionItem)
        {
            return (await Task.Run(async () => await completionService.GetDescriptionAsync(document, completionItem))).TaggedParts;
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
