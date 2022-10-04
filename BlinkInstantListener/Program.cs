using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections.Specialized;
using System.Collections;
using System.IO;
using XmlHandlingMockups;
using System.Xml;
using XmlMockups;

namespace BlinkInstantListener
{

    enum HttpAwaitedRequests
    {
        MessageUndefined,
        MessagePOST,
        MessageGET,
        MessageAck,
        Participants,
    }

    // Constants
    // ---------------



    class Program
    {
        private static Thread _listenerThread;
        private static HttpListener _httpListener;


        static void Main()
        {
            _httpListener = new HttpListener();
            _listenerThread = new Thread(HttpRequestProcessor.StartProcessor);
            _listenerThread.Start();

            for (; ; )
            {
                Console.WriteLine("Awaiting user input..");

                Console.WriteLine("Possible commands:");
                Console.WriteLine("\"1\" - Generate Pacs008 in C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\");
                Console.WriteLine("\"2\" - Exit");
                int choice = Int32.Parse(Console.ReadLine());

                switch (choice)
                {
                    case 1:
                        TestXml008Mockup pacs008 = new TestXml008Mockup();
                        pacs008.InitAndSaveDynamicDocument();
                        break;
                    case 2:
                        Environment.Exit(-1);
                        break;
                }
            }// for

        }
    }

    class HttpRequestProcessor
    {
        public static void StartProcessor()
        {


            HttpListener httpListener = new HttpListener();

            // We add the basic prefix of the request
            httpListener.Prefixes.Add("http://*:8080/");
            httpListener.Start();

            // Let's just log everything that is of value..
            Console.WriteLine("Listening...");
            for (; ; )
            {
                HttpListenerContext httpListenerContext = httpListener.GetContext();

                // We start a new thread so we can handle multiple requests
                new Thread(new HttpWorker(httpListenerContext).ProcessRequest).Start();
            }
        }
    }

    class HttpWorker
    {
        public const string _pacs008Constant = "FIToFICstmrCdtTrf";

        private HttpListenerContext httpListenerContext;

        public HttpWorker(HttpListenerContext httpListenerContext)
        {
            this.httpListenerContext = httpListenerContext;
        }

        public void ProcessRequest()
        {
            try
            {
                httpListenerContext.Response.StatusCode = (int)HttpStatusCode.OK;

                string receivedMessageURL = httpListenerContext.Request.HttpMethod + " " + httpListenerContext.Request.Url;
                Console.WriteLine(receivedMessageURL);

                HttpAwaitedRequests httpAwaitedRequest = HttpAwaitedRequests.MessageUndefined;

                bool isPostMessage = receivedMessageURL.Contains("POST");
                bool containsMessageAckPart = receivedMessageURL.Contains("MessageAck");

                if (containsMessageAckPart)
                    httpAwaitedRequest = HttpAwaitedRequests.MessageAck;
                else if (isPostMessage)
                    httpAwaitedRequest = HttpAwaitedRequests.MessagePOST;
                else
                    httpAwaitedRequest = HttpAwaitedRequests.MessageGET;

                httpListenerContext.Response.Headers.Clear();

                bool isBICFound = false;

                // We are searching for participant BIC. If we cannot find one we return bad request.
                foreach (string key in httpListenerContext.Request.Headers)
                {
                    if (key.Equals("X-MONTRAN-RTP-Channel") && httpListenerContext.Request.Headers[key].Length > 0)
                    {
                        isBICFound = true;
                        break;
                    }
                }

                if (!isBICFound)
                    httpListenerContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;

                if (!HandleRequest(httpAwaitedRequest, httpListenerContext.Request, httpListenerContext.Response))
                {
                    httpListenerContext.Response.Close();
                    return;
                }

                httpListenerContext.Response.Close();
            }
            catch
            {
                Console.WriteLine("Exception. Carry on.");
            }
        }

        private bool HandleRequest(HttpAwaitedRequests httpAwaitedRequest, HttpListenerRequest request, HttpListenerResponse response)
        {
            switch (httpAwaitedRequest)
            {

                case HttpAwaitedRequests.MessageAck:
                    // Won't be used for now
                    //if (!HandleAcknowledgeMessage(request, response) )
                    //    return false;
                    break;

                case HttpAwaitedRequests.MessageGET:
                    if (!HandleGETMessage(request, response))
                        return false;

                    break;

                case HttpAwaitedRequests.MessagePOST:
                    if (!HandlePOSTMessage(request, response))
                        return false;
                    break;

                case HttpAwaitedRequests.Participants:
                    // Won't be used for now
                    //response.StatusCode = (int)HttpStatusCode.BadRequest;
                    break;
            }

            return true;
        }

        private bool HandleAcknowledgeMessage(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            return true;
        }

        private bool HandlePOSTMessage(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                response.AddHeader("X-MONTRAN-RTP-Version", "1");

                System.IO.Stream body = request.InputStream;
                System.Text.Encoding encoding = request.ContentEncoding;
                System.IO.StreamReader reader = new System.IO.StreamReader(body, encoding);

                // Convert the data to a string and display it on the console.
                string inputXMLMessage = reader.ReadToEnd();

                // If we do not find pacs008 message tag, we return bad request
                if (!inputXMLMessage.Contains(_pacs008Constant))
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return false;
                }

                bool isToReturnACCPFor002 = GetValueForSetting("002ReturnACCP");
                bool returnBadRequest = GetValueForSetting("ReturnBadRequest");

                if (returnBadRequest)
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return false;
                }

                // Return 002 with ACCP
                if (isToReturnACCPFor002)
                {
                    response.AddHeader("X-MONTRAN-RTP-ReqSts", "ACCP");
                }
                else // return 002 with RJCT
                {
                    response.AddHeader("X-MONTRAN-RTP-ReqSts", "RJCT");
                }

                response.AddHeader("X-MONTRAN-RTP-MessageType", "pacs.002");

                string randomCharset = RandomCharset.Generate(20);

                XmlDocument xml008Doc = new XmlDocument();
                xml008Doc.LoadXml(inputXMLMessage);
                xml008Doc.Save("C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Service008Messages\\" + randomCharset + ".xml");

                TestXml002Mockup pacs002 = new TestXml002Mockup();
                pacs002.InitAndSaveDynamicDocument("C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Service008Messages\\" + randomCharset + ".xml", randomCharset);

                string filePath = "";
                string fileName = "";
                var directory = new DirectoryInfo(@"C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\");
                foreach (FileInfo file in directory.GetFiles())
                {
                    if (!file.Name.Contains(".Worked") && file.Name.Contains("002_Mock"))
                    {
                        filePath = file.FullName;
                        fileName = file.Name;
                        break;
                    }
                }

                if (fileName.Length <= 0)
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return false;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(filePath);
                string xmlContents = doc.InnerXml;

                if (!isToReturnACCPFor002 && xmlContents.Contains("<GrpSts>ACCP"))
                {
                    xmlContents = xmlContents.Replace("<GrpSts>ACCP", "<GrpSts>RJCT");
                }

                var sourcePath = @"C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\" + fileName;
                var destinationPath = @"C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\" + fileName + ".Worked";
                File.Move(sourcePath, destinationPath);

                byte[] encodedXML = Encoding.UTF8.GetBytes(xmlContents);
                response.ContentLength64 = encodedXML.Length;
                response.ContentEncoding = Encoding.UTF8;
                response.OutputStream.Write(encodedXML, 0, encodedXML.Length);
            }
            catch
            {
                Console.WriteLine("Post message exception. Carry on.");
            }

            return true;
        }

        private bool HandleGETMessage(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                response.AddHeader("X-MONTRAN-RTP-Version", "1");

                bool isToReturnNoMessagesFound = GetValueForSetting("ReturnNoMessagesForGetRequest");

                if (isToReturnNoMessagesFound)
                    response.AddHeader("X-MONTRAN-RTP-ReqSts", "EMPTY");

                bool isToReturnPossibleDuplicate = GetValueForSetting("ReturnPossibleDuplicateMessage");

                if (isToReturnPossibleDuplicate)
                    response.AddHeader("X-MONTRAN-RTP-PossibleDuplicate ", "true");

                string filePath = "";
                string fileName = "";
                var directory = new DirectoryInfo(@"C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\");
                foreach (FileInfo file in directory.GetFiles())
                {
                    if (!file.Name.Contains(".Worked") && file.Name.Contains("008_Mock"))
                    {
                        filePath = file.FullName;
                        fileName = file.Name;
                        break;
                    }
                }

                if (filePath.Length > 0)
                {
                    try
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(filePath);

                        string xmlContents = doc.InnerXml;

                        var sourcePath = @"C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\" + fileName;
                        var destinationPath = @"C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\" + fileName + ".Worked";
                        File.Move(sourcePath, destinationPath);

                        byte[] encodedXML = Encoding.UTF8.GetBytes(xmlContents);
                        response.ContentLength64 = encodedXML.Length;
                        response.ContentEncoding = Encoding.UTF8;
                        response.OutputStream.Write(encodedXML, 0, encodedXML.Length);
                    }
                    catch
                    {
                        Console.WriteLine("Lot's of GET messages..");
                    }
                }

                // Add Message Type header depending on the xml
                // 002 for now
                response.AddHeader("X-MONTRAN-RTP-MessageType", "pacs.002");
            }
            catch
            {
                Console.WriteLine("Get Message Exception. Carry on.");
            }

            return true;
        }

        private bool GetValueForSetting(string targetSetting)
        {
            bool value = false;
            string settingsLine = "";

            System.IO.StreamReader settingsFile = new System.IO.StreamReader("WebAPISettings.txt");
            while ((settingsLine = settingsFile.ReadLine()) != null)
            {
                if (settingsLine.Contains(targetSetting))
                {
                    string settingsValue = settingsLine.Substring(settingsLine.IndexOf('=') + 1);
                    value = Convert.ToBoolean(settingsValue);
                }
            }

            settingsFile.Close();

            return value;
        }

    }
}

namespace XmlHandlingMockups
{
    enum XmlSetupType
    {
        Append,
        Override
    }

    enum XmlSearchType
    {
        Path,
        Tag
    }

    class XmlItemValue
    {
        public string value;
        public XmlSetupType xmlSetupType;

        public XmlItemValue()
        {
            value = "";
            xmlSetupType = XmlSetupType.Override;
        }

        public XmlItemValue(string value, XmlSetupType xmlSetupType = XmlSetupType.Override)
        {
            this.value = value;
            this.xmlSetupType = xmlSetupType;
        }
    }

    class XmlSearchItem
    {
        public string tagName;
        public XmlSearchType xmlSearchType;

        public XmlSearchItem()
        {
            tagName = "";
            xmlSearchType = XmlSearchType.Tag;
        }

        public XmlSearchItem(string tagName, XmlSearchType xmlSearchType = XmlSearchType.Tag)
        {
            this.tagName = tagName;
            this.xmlSearchType = xmlSearchType;
        }
    }

    class XmlDynamicItem
    {
        public XmlSearchItem xmlSearchItem;
        public XmlItemValue xmlItemValue;

        public XmlDynamicItem()
        {
            xmlSearchItem = new XmlSearchItem();
            xmlItemValue = new XmlItemValue();
        }

        public XmlDynamicItem(XmlSearchItem xmlSearchItem, XmlItemValue xmlItemValue)
        {
            this.xmlSearchItem = xmlSearchItem;
            this.xmlItemValue = xmlItemValue;
        }
    }

    class XmlMockupDocument
    {
        public XmlDocument xmlDocument;
        public XmlNamespaceManager xmlNamespaceManager;

        public XmlMockupDocument()
        {
            xmlDocument = new XmlDocument();
        }
    }

    class XmlCopyBinding
    {
        public XmlSearchItem xmlSearchItemSrc;
        public XmlDynamicItem xmlSearchItemDest;
        public XmlSetupType xmlSetupTypeDest;

        public XmlCopyBinding()
        {
            xmlSearchItemSrc = new XmlSearchItem();
            xmlSearchItemDest = new XmlDynamicItem();
            xmlSetupTypeDest = XmlSetupType.Override;
        }


        public XmlCopyBinding(XmlSearchItem xmlSearchItemSrc, XmlDynamicItem xmlSearchItemDest, XmlSetupType xmlSetupTypeDest = XmlSetupType.Override)
        {
            this.xmlSearchItemSrc = xmlSearchItemSrc;
            this.xmlSearchItemDest = xmlSearchItemDest;
            this.xmlSetupTypeDest = xmlSetupTypeDest;
        }
    }

    abstract class XmlMockup
    {
        protected XmlMockupDocument m_xmlMockupDocument;
        //protected XmlDynamicItem[] m_xmlDynamicItems;
        protected List<XmlDynamicItem> m_xmlDynamicItems;

        public XmlMockup()
        {
            m_xmlMockupDocument = new XmlMockupDocument();
            m_xmlDynamicItems = new List<XmlDynamicItem>();
        }

        public abstract void InitDocument();

        public abstract void InitNamespace();

        public abstract void InitDynamicItems();

        public virtual XmlMockupDocument GetMockupDocument()
        {
            return m_xmlMockupDocument;
        }

        public virtual XmlDynamicItem[] GetDynamicItems()
        {
            return m_xmlDynamicItems.ToArray();
        }

        public virtual XmlDocument GetDocument()
        {
            return m_xmlMockupDocument.xmlDocument;
        }

        public virtual XmlNamespaceManager GetNamespaceManager()
        {
            return m_xmlMockupDocument.xmlNamespaceManager;
        }
    }

    class XmlMockupHandler
    {
        public static bool InitDynamicItems(XmlMockupDocument xmlMockupDocument, XmlDynamicItem[] xmlDynamicItems)
        {
            foreach (XmlDynamicItem xmlDynamicItem in xmlDynamicItems)
            {
                XmlNodeList xmlNodeList = xmlMockupDocument.xmlDocument.ChildNodes;
                xmlNodeList = GetNodes(xmlMockupDocument, xmlDynamicItem.xmlSearchItem);

                SetNodesValue(xmlNodeList, xmlDynamicItem.xmlItemValue.xmlSetupType, xmlDynamicItem.xmlItemValue.value);
            }

            return true;
        }

        public static bool InitDynamicItems(XmlMockup xmlMockup)
        {
            xmlMockup.InitDynamicItems();

            if (!InitDynamicItems(xmlMockup.GetMockupDocument(), xmlMockup.GetDynamicItems()))
                return false;

            return true;
        }

        public static bool InitDynamicItems<XmlMockupProvider>() where XmlMockupProvider : XmlMockup, new()
        {
            XmlMockupProvider xmlMockupProvider = new XmlMockupProvider();

            xmlMockupProvider.InitDocument();
            xmlMockupProvider.InitNamespace();
            xmlMockupProvider.InitDynamicItems();

            if (!InitDynamicItems(xmlMockupProvider.GetMockupDocument(), xmlMockupProvider.GetDynamicItems()))
                return false;

            return true;
        }

        public static bool CloneItems(XmlMockupDocument xmlDocumentSrc, XmlMockupDocument xmlDocumentDest, XmlCopyBinding[] xmlCopyBindings)
        {
            foreach (XmlCopyBinding xmlCopyBinding in xmlCopyBindings)
            {

                XmlNodeList xmlSrcNodeList = xmlDocumentSrc.xmlDocument.ChildNodes;
                XmlNodeList xmlDestNodeList = xmlDocumentDest.xmlDocument.ChildNodes;

                xmlSrcNodeList = GetNodes(xmlDocumentSrc, xmlCopyBinding.xmlSearchItemSrc);
                xmlDestNodeList = GetNodes(xmlDocumentDest, xmlCopyBinding.xmlSearchItemDest.xmlSearchItem);

                //Ако няма или има повече от един елемент в източника(src), не може да се определи коя стойност да се извлече
                if (xmlSrcNodeList.Count == 0)
                    continue;

                //Проверява дали има елементи с такъв таг в current документа
                if (xmlDestNodeList.Count == 0)
                    continue;

                XmlNode xmlNode = xmlSrcNodeList[0]; //извличаме единственият елемент

                SetNodesValue(xmlDestNodeList, xmlCopyBinding.xmlSetupTypeDest, xmlNode.InnerText);
                xmlCopyBinding.xmlSearchItemDest.xmlItemValue.value = xmlNode.InnerText;
            }

            return true;
        }

        public static XmlNodeList GetNodes(XmlMockupDocument xmlMockupDocument, XmlSearchItem xmlSearchItem)
        {
            XmlNodeList xmlNodeList = xmlMockupDocument.xmlDocument.ChildNodes;

            switch (xmlSearchItem.xmlSearchType)
            {
                case XmlSearchType.Path:
                    {
                        xmlNodeList = xmlMockupDocument.xmlDocument.SelectNodes(xmlSearchItem.tagName, xmlMockupDocument.xmlNamespaceManager);
                    }
                    break;
                case XmlSearchType.Tag:
                    {
                        xmlNodeList = xmlMockupDocument.xmlDocument.GetElementsByTagName(xmlSearchItem.tagName);
                    }
                    break;
            }

            return xmlNodeList;
        }

        private static void SetNodesValue(XmlNodeList xmlNodeList, XmlSetupType xmlSetupType, string value)
        {
            foreach (XmlNode xmlNode in xmlNodeList)
            {
                switch (xmlSetupType)
                {
                    case XmlSetupType.Append:
                        {
                            xmlNode.InnerText += value;
                        }
                        break;
                    case XmlSetupType.Override:
                        {
                            xmlNode.InnerText = value;
                        }
                        break;
                }
            }
        }
    }
}

namespace XmlMockups
{
    public static class RandomCharset
    {
        static readonly Random random = new Random();

        public static string Generate(int length)
        {
            const string characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            StringBuilder result = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                result.Append(characters[random.Next(characters.Length)]);
            }

            return result.ToString();
        }

        public static string GetRandomDecimal(int randTo, int round, string formatSpecifier = "F")
        {
            double number = random.NextDouble() * randTo;
            decimal roundNumber = new decimal(Math.Round(number, round));

            return roundNumber.ToString(formatSpecifier);
        }
    }

    class Xml008Mockup : XmlMockup
    {
        public XmlDynamicItem TxId;

        public override void InitDocument()
        {
            m_xmlMockupDocument.xmlDocument.Load("C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups\\PaymentTransfer_008_Mock.xml");
        }

        public override void InitNamespace()
        {
            m_xmlMockupDocument.xmlNamespaceManager = new XmlNamespaceManager(m_xmlMockupDocument.xmlDocument.NameTable);

            m_xmlMockupDocument.xmlNamespaceManager.AddNamespace("Message", "urn:montran:message.01"); //hdr:Message
            m_xmlMockupDocument.xmlNamespaceManager.AddNamespace("AppHdr", "urn:iso:std:iso:20022:tech:xsd:head.001.001.01"); //hdr:AppHdr
            m_xmlMockupDocument.xmlNamespaceManager.AddNamespace("FIToFICstmrCdtTrf", "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.02"); //hdr:FIToFICstmrCdtTrf
        }

        public override void InitDynamicItems()
        {
            XmlDynamicItem CreDt = new XmlDynamicItem();
            CreDt.xmlSearchItem = new XmlSearchItem("CreDt");

            string CreDtValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            CreDt.xmlItemValue = new XmlItemValue(CreDtValue);

            XmlDynamicItem MsgId = new XmlDynamicItem();
            MsgId.xmlSearchItem = new XmlSearchItem("MsgId");

            string MsgIdValue = RandomCharset.Generate(17);
            MsgId.xmlItemValue = new XmlItemValue(MsgIdValue);

            XmlDynamicItem CreDtTm = new XmlDynamicItem();
            CreDtTm.xmlSearchItem = new XmlSearchItem("CreDtTm");

            string CreDtTmValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+03:00");
            CreDtTm.xmlItemValue = new XmlItemValue(CreDtTmValue);

            XmlDynamicItem TtlIntrBkSttlmAmt = new XmlDynamicItem();
            TtlIntrBkSttlmAmt.xmlSearchItem = new XmlSearchItem("TtlIntrBkSttlmAmt");

            string TtlIntrBkSttlmAmtValue = RandomCharset.GetRandomDecimal(100, 2);
            TtlIntrBkSttlmAmt.xmlItemValue = new XmlItemValue(TtlIntrBkSttlmAmtValue);

            XmlDynamicItem IntrBkSttlmDt = new XmlDynamicItem();
            IntrBkSttlmDt.xmlSearchItem = new XmlSearchItem("IntrBkSttlmDt");

            string IntrBkSttlmDtValue = DateTime.Now.ToString("yyyy-MM-dd");
            IntrBkSttlmDt.xmlItemValue = new XmlItemValue(IntrBkSttlmDtValue);

            XmlDynamicItem EndToEndId = new XmlDynamicItem();
            EndToEndId.xmlSearchItem = new XmlSearchItem("EndToEndId");

            string EndToEndIdValue = RandomCharset.Generate(17);
            EndToEndId.xmlItemValue = new XmlItemValue(EndToEndIdValue);

            TxId = new XmlDynamicItem();
            TxId.xmlSearchItem = new XmlSearchItem("TxId");

            string TxIdValue = RandomCharset.Generate(10);
            TxId.xmlItemValue = new XmlItemValue(TxIdValue);

            XmlDynamicItem IntrBkSttlmAmt = new XmlDynamicItem();
            IntrBkSttlmAmt.xmlSearchItem = new XmlSearchItem("IntrBkSttlmAmt");

            string IntrBkSttlmAmtValue = RandomCharset.GetRandomDecimal(100, 2);
            IntrBkSttlmAmt.xmlItemValue = new XmlItemValue(IntrBkSttlmAmtValue);

            XmlDynamicItem AccptncDtTm = new XmlDynamicItem();
            AccptncDtTm.xmlSearchItem = new XmlSearchItem("AccptncDtTm");

            string AccptncDtTmValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+03:00");
            AccptncDtTm.xmlItemValue = new XmlItemValue(AccptncDtTmValue);

            XmlDynamicItem DbtrAcctIBAN = new XmlDynamicItem();
            DbtrAcctIBAN.xmlSearchItem = new XmlSearchItem("//Message:FIToFICstmrCdtTrf//FIToFICstmrCdtTrf:DbtrAcct//FIToFICstmrCdtTrf:IBAN", XmlSearchType.Path);

            string DbtrAcctIBANValue = "DE45100700000025970500";
            DbtrAcctIBAN.xmlItemValue = new XmlItemValue(DbtrAcctIBANValue);

            XmlDynamicItem CdtrAcctIBAN = new XmlDynamicItem();
            CdtrAcctIBAN.xmlSearchItem = new XmlSearchItem("//Message:FIToFICstmrCdtTrf//FIToFICstmrCdtTrf:CdtrAcct//FIToFICstmrCdtTrf:IBAN", XmlSearchType.Path);

            string CdtrAcctIBANValue = "BG90BPBI79451068211401";
            CdtrAcctIBAN.xmlItemValue = new XmlItemValue(CdtrAcctIBANValue);

            m_xmlDynamicItems.Add(CreDt);
            m_xmlDynamicItems.Add(MsgId);
            m_xmlDynamicItems.Add(CreDtTm);
            m_xmlDynamicItems.Add(TtlIntrBkSttlmAmt);
            m_xmlDynamicItems.Add(IntrBkSttlmDt);
            m_xmlDynamicItems.Add(EndToEndId);
            m_xmlDynamicItems.Add(TxId);
            m_xmlDynamicItems.Add(IntrBkSttlmAmt);
            m_xmlDynamicItems.Add(AccptncDtTm);
            m_xmlDynamicItems.Add(DbtrAcctIBAN);
            m_xmlDynamicItems.Add(CdtrAcctIBAN);
        }
    }

    class Xml002Mockup : XmlMockup
    {
        public override void InitDocument()
        {
            m_xmlMockupDocument.xmlDocument.Load("C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups\\PaymentConfirmation_002_Mock.xml");
        }

        public override void InitNamespace()
        {
            m_xmlMockupDocument.xmlNamespaceManager = new XmlNamespaceManager(m_xmlMockupDocument.xmlDocument.NameTable);

            m_xmlMockupDocument.xmlNamespaceManager.AddNamespace("Message", "urn:montran:message.01"); //hdr:Message
            m_xmlMockupDocument.xmlNamespaceManager.AddNamespace("AppHdr", "urn:iso:std:iso:20022:tech:xsd:head.001.001.01");  //hdr:AppHdr
            m_xmlMockupDocument.xmlNamespaceManager.AddNamespace("FIToFIPmtStsRpt", "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.03"); //hdr:FIToFIPmtStsRpt
        }

        public override void InitDynamicItems()
        {
            XmlDynamicItem CreDt = new XmlDynamicItem();
            CreDt.xmlSearchItem = new XmlSearchItem("CreDt");

            string CreDtValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            CreDt.xmlItemValue = new XmlItemValue(CreDtValue);

            XmlDynamicItem MsgId = new XmlDynamicItem();
            MsgId.xmlSearchItem = new XmlSearchItem("MsgId");

            string MsgIdValue = RandomCharset.Generate(17);
            MsgId.xmlItemValue = new XmlItemValue(MsgIdValue);

            XmlDynamicItem CreDtTm = new XmlDynamicItem();
            CreDtTm.xmlSearchItem = new XmlSearchItem("CreDtTm");

            string CreDtTmValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+03:00");
            CreDtTm.xmlItemValue = new XmlItemValue(CreDtTmValue);

            XmlDynamicItem StsId = new XmlDynamicItem();
            StsId.xmlSearchItem = new XmlSearchItem("StsId");

            string StsIdValue = RandomCharset.Generate(16);
            StsId.xmlItemValue = new XmlItemValue(StsIdValue); //Random или стойността като MsgId?

            XmlDynamicItem AccptncDtTm = new XmlDynamicItem();
            AccptncDtTm.xmlSearchItem = new XmlSearchItem("AccptncDtTm");

            string AccptncDtTmValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+03:00");
            AccptncDtTm.xmlItemValue = new XmlItemValue(AccptncDtTmValue);

            m_xmlDynamicItems.Add(CreDt);
            m_xmlDynamicItems.Add(MsgId);
            m_xmlDynamicItems.Add(CreDtTm);
            m_xmlDynamicItems.Add(StsId);
            m_xmlDynamicItems.Add(AccptncDtTm);
        }
    }

    class TestXml008Mockup
    {
        public string InitAndSaveDynamicDocument()
        {
            Xml008Mockup xml008Mockup = new Xml008Mockup();
            xml008Mockup.InitDocument();
            xml008Mockup.InitNamespace();

            XmlMockupHandler.InitDynamicItems(xml008Mockup);

            string fileToSave = "C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output" + Path.DirectorySeparatorChar + xml008Mockup.TxId.xmlItemValue.value + "_008_Mock_Output.xml";
            xml008Mockup.GetMockupDocument().xmlDocument.Save(fileToSave);

            return fileToSave;
        }
    }

    class TestXml002Mockup
    {
        public void InitAndSaveDynamicDocument(string fileNameToRead, string filenamePrefix)
        {
            Xml002Mockup xml002Mockup = new Xml002Mockup();

            xml002Mockup.InitDocument();
            xml002Mockup.InitNamespace();

            XmlMockupHandler.InitDynamicItems(xml002Mockup);

            XmlDynamicItem OrgnlTxId = new XmlDynamicItem();
            OrgnlTxId.xmlSearchItem = new XmlSearchItem("OrgnlTxId");

            XmlDynamicItem OrgnlMsgId = new XmlDynamicItem();
            OrgnlMsgId.xmlSearchItem = new XmlSearchItem("OrgnlMsgId");

            XmlDynamicItem OrgnlEndToEndId = new XmlDynamicItem();
            OrgnlEndToEndId.xmlSearchItem = new XmlSearchItem("OrgnlEndToEndId");

            XmlCopyBinding[] xmlCopyBindings = new XmlCopyBinding[]
            {
                new XmlCopyBinding(new XmlSearchItem("MsgId"), OrgnlMsgId),
                new XmlCopyBinding(new XmlSearchItem("EndToEndId"), OrgnlEndToEndId),
                new XmlCopyBinding(new XmlSearchItem("TxId"), OrgnlTxId),
            };

            Xml008Mockup xml008Mockup = new Xml008Mockup();
            xml008Mockup.GetDocument().Load(fileNameToRead);
            xml008Mockup.InitNamespace();

            XmlMockupHandler.CloneItems(xml008Mockup.GetMockupDocument(), xml002Mockup.GetMockupDocument(), xmlCopyBindings);

            string fileToSave = "C:\\CSOFT_EXE\\Services\\VCSBankBlinkInstantService\\Listener\\Mockups Output\\" + filenamePrefix + "_002_Mock_Output.xml";
            xml002Mockup.GetMockupDocument().xmlDocument.Save(fileToSave);
        }
    }
}
