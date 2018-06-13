

namespace iCSharp.Kernel.Shell
{
	using Common.Logging;
	using System.Collections.Generic;
	using Common.Serializer;
	using iCSharp.Messages;
	using NetMQ.Sockets;
	using iCSharp.Kernel.Helpers;
	using System.Text.RegularExpressions;
	using System.IO;
	using System.Reflection;
	using System;
	using System.Linq;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis;
    
    public class CompleteRequestHandler : IShellMessageHandler
	{
		private ILog logger;
		private readonly IMessageSender messageSender;

        public CompleteRequestHandler(ILog logger, IMessageSender messageSender)
		{
			this.logger = logger;
			this.messageSender = messageSender;
		}
        
        public struct methodCollection
        {
            public MethodDeclarationSyntax mds;
            public string className;
        }

        public struct variableCollection
        {
            public VariableDeclaratorSyntax variableDeclaratorSyntax;
            public string className;
        }

        public struct catchCollection
        {
            public string modifier;
            public string className;
            public string name;
            public string documentation;
        }

        public void HandleMessage(Message message, RouterSocket serverSocket, PublisherSocket ioPub)
		{
			CompleteRequest completeRequest = JsonSerializer.Deserialize<CompleteRequest>(message.Content);

            /// Lists
            List<CompleteReplyMatch> DirectiveMatches = new List<CompleteReplyMatch>();
            List<CompleteReplyMatch> matches_ = new List<CompleteReplyMatch>();
            List<string> listOfKeywords = new List<string>();
            List<ClassDeclarationSyntax> classList = new List<ClassDeclarationSyntax>();
            List<methodCollection> methodList = new List<methodCollection>();
            List<CompleteReplyMatch> interfaceList = new List<CompleteReplyMatch>();
            List<catchCollection> enumList = new List<catchCollection>();
            List<catchCollection> structList = new List<catchCollection>();
            List<catchCollection> propertyList = new List<catchCollection>();
            List<variableCollection> variableList = new List<variableCollection>();
            List<CompleteReplyMatch> FinalDirectives = new List<CompleteReplyMatch>();

            string line = completeRequest.Line;
			line = line.Substring(1, completeRequest.CursorPosition);

            foreach (var codes in completeRequest.CodeCells)
            {
                var code = Regex.Replace(codes.Substring(1, codes.Length - 2), @"\\n", "*");
                var tree = CSharpSyntaxTree.ParseText(code);
                var syntaxRoot = tree.GetRoot();

                CatchClassMethods(tree, ref classList, completeRequest.CodePosition, ref methodList, ref variableList);
                catchInterfaces(syntaxRoot, ref interfaceList, classList);
                catchEnums(syntaxRoot, ref enumList, classList);
                catchStructs(syntaxRoot, ref structList, classList);
                catchVariables(syntaxRoot, ref variableList, classList);
                CatchMethods(syntaxRoot, ref methodList, classList);
            }

            string currentClass = insideBody(classList, completeRequest.CodePosition);

            if (currentClass.Equals("global"))
            {
                matches_.AddRange(interfaceList);
            }

            foreach (var item in classList)
            {

                CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch();
                completeReplyMatch.Documentation = "";
                completeReplyMatch.Name = item.Identifier.ToString();
                completeReplyMatch.Glyph = "class";
                completeReplyMatch.Value = "";

                matches_.Add(completeReplyMatch);

            }

            foreach (var item in methodList)
            {
                CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch();
                completeReplyMatch.Documentation = "";
                completeReplyMatch.Name = item.mds.Identifier.ToString();
                completeReplyMatch.Glyph = "method";
                completeReplyMatch.Value = "";

                matches_.Add(completeReplyMatch);

            }

            foreach (var item in variableList)
            {
                CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch();

                completeReplyMatch.Documentation = "";
                completeReplyMatch.Name = item.variableDeclaratorSyntax.Identifier.ToString();
                completeReplyMatch.Glyph = "";
                completeReplyMatch.Value = "";

                matches_.Add(completeReplyMatch);

            }

            foreach (var item in enumList)
            {
                CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch();
                completeReplyMatch.Documentation = "";
                completeReplyMatch.Name = item.name.ToString();
                completeReplyMatch.Glyph = "enum";
                completeReplyMatch.Value = "";

                matches_.Add(completeReplyMatch);

            }

            foreach (var item in structList)
            {
                CompleteReplyMatch completeReplyMatch = new CompleteReplyMatch();
                completeReplyMatch.Documentation = "";
                completeReplyMatch.Name = item.name.ToString();
                completeReplyMatch.Glyph = "struct";
                completeReplyMatch.Value = "";

                matches_.Add(completeReplyMatch);

            }


            AddKeywordsToMatches(ref matches_);

            Tuple<string, int> cursorInfo;
            cursorInfo = FindWordToAutoComplete(line);

            string cursorWord = cursorInfo.Item1;
            int cursorWordLength = cursorInfo.Item2;

            int ReplacementStartPosition = completeRequest.CursorPosition - cursorWordLength;

            RemoveNonMatches(ref matches_, cursorWord, line);

            CompleteReply completeReply = new CompleteReply();

            completeReply.Matches = matches_;
            completeReply.Status = "ok";
            completeReply.FilterStartIndex = ReplacementStartPosition;
            completeReply.MatchedText = cursorWord;

            Message completeReplyMessage = MessageBuilder.CreateMessage(MessageTypeValues.CompleteReply, JsonSerializer.Serialize(completeReply), message.Header);
			this.logger.Info("Sending complete_reply");
			this.messageSender.Send(completeReplyMessage, serverSocket);

		}


        public void catchInterfaces(SyntaxNode tree, ref List<CompleteReplyMatch> interfaceList, List<ClassDeclarationSyntax> classes)
        {
            var myInterface = tree.DescendantNodes().OfType<InterfaceDeclarationSyntax>().ToList();

            foreach (var node in myInterface)
            {
                CompleteReplyMatch crm = new CompleteReplyMatch()
                {
                    Name = node.Identifier.ToString(),// m.Groups["classname"].ToString(),
                    Documentation = "<font style=\"color:blue\">interface</font>" + node.Identifier.ToString(),
                    Value = "",
                    Glyph = "interface"
                };
                interfaceList.Add(crm);
            }
        }

        public void catchEnums(SyntaxNode tree, ref List<catchCollection> enumList, List<ClassDeclarationSyntax> classes)
        {
            var myEnum = tree.DescendantNodes().OfType<EnumDeclarationSyntax>().ToList();
            catchCollection collection;
            foreach (var node in myEnum)
            {
                collection.documentation = "<font style=\"color:blue\">enum</font>" + node.Identifier.ToString();
                collection.name = node.Identifier.ToString();
                collection.modifier = node.Modifiers.ToString();
                collection.className = "global";

                foreach (var c in classes)
                {
                    if ((node.Span.Start >= c.Span.Start) && (node.Span.End <= c.Span.End))
                    {

                        collection.className = c.Identifier.ToString();

                    }
                }
                enumList.Add(collection);
            }
        }

        public void catchStructs(SyntaxNode tree, ref List<catchCollection> structList, List<ClassDeclarationSyntax> classes)
        {
            var myStruct = tree.DescendantNodes().OfType<StructDeclarationSyntax>().ToList();
            catchCollection collection;
            foreach (var node in myStruct)
            {
                collection.className = "global";
                collection.documentation = "<font style=\"color:blue\">struct</font>" + node.Identifier.ToString();
                collection.modifier = node.Modifiers.ToString();
                collection.name = node.Identifier.ToString();
                foreach (var c in classes)
                {
                    if ((node.Span.Start >= c.Span.Start) && (node.Span.End <= c.Span.End))
                    {
                        collection.className = c.Identifier.ToString();
                    }
                }
                structList.Add(collection);
            }
        }
        // Find variables which are not inside methods, classes or structs

        public void catchVariables(SyntaxNode tree, ref List<variableCollection> variableList, List<ClassDeclarationSyntax> classes)
        {
            var myVariable = tree.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToList();
            variableCollection collection;
            foreach (var node in myVariable)
            {
                collection.variableDeclaratorSyntax = node;
                collection.className = "global";

                foreach (var c in classes)
                {
                    if (!(node.Span.Start >= c.Span.Start) && (node.Span.End <= c.Span.End))
                    {
                        collection.className = c.Identifier.ToString();
                        variableList.Add(collection);
                    }

                }
            }
        }

        public void CatchClassMethods(SyntaxTree tree, ref List<ClassDeclarationSyntax> classList, int curPos, ref List<methodCollection> methods, ref List<variableCollection> variableList)
        {

            var root = (CompilationUnitSyntax)tree.GetRoot();

            var compilation = CSharpCompilation.Create("HelloWorld")
                                               .AddReferences(
                                                    MetadataReference.CreateFromFile(
                                                        typeof(object).Assembly.Location))
                                               .AddSyntaxTrees(tree);

            var model = compilation.GetSemanticModel(tree);

            List<ClassDeclarationSyntax> classListCaptured = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();


            foreach (var l in classListCaptured)
            {
                classList.Add(l);
                bool currentClass = false;

                if ((l.Span.Start <= curPos) && (l.Span.End >= curPos))
                {
                    currentClass = true;
                }
                // Catch public class methods
                foreach (var meths in l.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList())
                {
                    if (meths.Modifiers.ToString().Equals("public") || currentClass == true)
                    {

                        methods.Add(new methodCollection()
                        {
                            className = l.Identifier.ToString(),
                            mds = meths,
                        });
                    }
                }

                foreach (var vars in l.DescendantNodes().OfType<VariableDeclarationSyntax>().ToList())
                {
                    Console.WriteLine("Field Declared:" + vars.Ancestors().OfType<FieldDeclarationSyntax>().First().Modifiers.ToString());
                    
                    if (vars.Ancestors().OfType<FieldDeclarationSyntax>().First().Modifiers.ToString().Equals("public") || currentClass == true)
                    {
                        variableList.Add(new variableCollection()
                        {
                            className = l.Identifier.ToString(),
                            variableDeclaratorSyntax = vars.DescendantNodes().OfType<VariableDeclaratorSyntax>().First()

                        });
                    }
                }

                // If currentclass
                if ((l.Span.Start <= curPos) && (l.Span.End >= curPos))
                {

                    // Catch class members

                }
            }
        }

        public void CatchMethods(SyntaxNode tree, ref List<methodCollection> methods, List<ClassDeclarationSyntax> classes)
        {

            var myMethod = tree.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            methodCollection collection;

            foreach (var node in myMethod)
            {
                collection.className = "global";
                collection.mds = node;

                foreach (var c in classes)
                {
                    if (!(node.Span.Start >= c.Span.Start) && (node.Span.End <= c.Span.End))
                    {
                        collection.className = c.Identifier.ToString();
                        methods.Add(collection);

                    }
                }
            }

        }




        public void AddKeywordsToMatches(ref List<CompleteReplyMatch> matches_)
        {

            string[] arrayOfKeywords = { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "using static", "virtual", "void", "volatile", "while" };
            List<string> listOfKeywords = new List<string>();
            listOfKeywords.AddRange(arrayOfKeywords);

            foreach (string i in listOfKeywords)
            {
                CompleteReplyMatch crm = new CompleteReplyMatch()
                {

                    Name = i,
                    Documentation = "<font style=\"color:blue\">" + i + "</font> " + " Keyword",
                    Value = "",
                    Glyph = "keyword"

                };
                matches_.Add(crm);

            }
        }

        public Tuple<string, int> FindWordToAutoComplete(string line)
        {
            line = Regex.Replace(line, @"[^\w&^\.]", "*");

            string cursorWord, cursorLine;
            int curWordLength = 0;

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
            if (cursorLine.Length > 0)
            {
                if (cursorLine[cursorLine.Length - 1] == '.')
                {
                    cursorLine = cursorLine.Substring(0, cursorLine.Length - 1);
                }
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

            curWordLength = cursorWord.Length;

            if (line.Length > 0)
            {
                if (line[line.Length - 1] == '.')
                {
                    curWordLength = 0;
                }
            }

            return Tuple.Create(cursorWord, curWordLength);

        }

        public void RemoveNonMatches(ref List<CompleteReplyMatch> matches_, string cursorWord, string line)
        {

            for (int j = matches_.Count - 1; j > -1; j--)
            {
                if ((line.StartsWith("using ")) && (line[line.Length - 1] == '.'))
                {
                    return;
                }
                if (!(matches_[j].Name.StartsWith(cursorWord)))
                {
                    matches_.RemoveAt(j);
                }
            }
        }

        public void ShowMethods(Type type, ref List<CompleteReplyMatch> matches_)
        {
            foreach (var method in type.GetMethods())
            {
                var parameters = method.GetParameters();
                var parameterDescriptions = string.Join
                    (", ", method.GetParameters()
                                 .Select(x => x.ParameterType + " " + x.Name)
                                 .ToArray());

                CompleteReplyMatch crm = new CompleteReplyMatch
                {

                    Name = method.Name,
                    Documentation = parameterDescriptions,
                    Value = "",
                    Glyph = "method"
                };
                matches_.Add(crm);

            }
        }

        public void DirectivesList(ref List<CompleteReplyMatch> DirectiveMatches, string line)
        {
            Regex regex = new Regex(@"(using)([\s]+)(?<directive>(\w|\.)*)");
            Match match = regex.Match(line);
            string input = match.Groups["directive"].ToString();

           
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
   

                    var namespaces = assembly.GetTypes()
                         .Select(t => GetToNearestDot(t, input))
                         .Distinct();

                    foreach (var n in namespaces)
                    {
                        if (!(n.Equals(" ")))
                        {
                            CompleteReplyMatch crm = new CompleteReplyMatch
                            {
                                Name = n,
                                Documentation = "",
                                Value = "",
                                Glyph = "directive"

                            };
                            DirectiveMatches.Add(crm);
                        }
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    Console.WriteLine("Could not load type");
                }
            }
        }

        public string GetToNearestDot(Type t, string s)
        {

            try
            {
                if (!(t.Namespace.StartsWith(s)))
                {
                    return " ";
                }
            }
            catch (NullReferenceException e)
            {
                return " ";
            }


            string ns = t.Namespace ?? "";

            int firstDot = s.LastIndexOf('.');;
            if (ns.Equals(""))
            {

            }
            else
            {
                ns = ns.Substring(firstDot + 1);
            }

            int firstDotns = ns.IndexOf('.');

            int finaldot = ns.IndexOf('.');

            return (finaldot == -1) ? ns : ns.Substring(0, finaldot);

        }


        string insideBody(List<ClassDeclarationSyntax> classList, int code_pos)
        {
            foreach (var i in classList)
            {
                if ((i.Span.Start <= code_pos) && (i.Span.End >= code_pos))
                {
                    return i.Identifier.ToString();
                }
            }
            return "global";
        }
	}
}

