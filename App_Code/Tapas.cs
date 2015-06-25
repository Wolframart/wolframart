using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Net.Http.Headers;
using System.Globalization;
using Examine;
using Umbraco.Web;
using Umbraco.Web.WebApi;
using Umbraco.Web.Models;
using Umbraco.Core.Models;
using umbraco.MacroEngines.Library;
using umbraco.NodeFactory;

public class MediaValues
{
    public MediaValues(XPathNavigator xpath)
    {
        if (xpath == null) throw new ArgumentNullException("xpath");
        Name = xpath.GetAttribute("nodeName", "");
        Values = new Dictionary<string, string>();
        var result = xpath.SelectChildren(XPathNodeType.Element);
        while (result.MoveNext())
        {
            if (result.Current != null && !result.Current.HasAttributes)
            {
                Values.Add(result.Current.Name, result.Current.Value);
            }
        }

        if (Values.Keys.Contains("error"))
        {
            NiceUrl = "";
        }
        else
        {
            NiceUrl = Values["umbracoFile"];
        }
    }
    public MediaValues(SearchResult result)
    {
        if (result == null) throw new ArgumentNullException("result");
        Name = result.Fields["nodeName"];
        Values = result.Fields;
        NiceUrl = Values["umbracoFile"];
    }

    public MediaValues()
    {
        Name = "";
        NiceUrl = "";
        Values = new Dictionary<string, string>()
            {
                {"umbracoFile", ""},
                {"umbracoWidth", ""},
                {"umbracoHeight", ""},
                {"umbracoBytes", ""},
                {"umbracoExtension", ""}
            };
    }

    public string Name { get; private set; }
    public string NiceUrl { get; private set; }
    public IDictionary<string, string> Values { get; private set; }
}

public static class Tapas
{
    public static MediaValues GetMedia(object id)
    {
        int iid;
        if (id == null || !Int32.TryParse(id.ToString(), out iid))
        {
            return new MediaValues();
        }

        //first check in Examine as this is WAY faster
        var criteria = ExamineManager.Instance
            .SearchProviderCollection["InternalSearcher"]
            .CreateSearchCriteria("media");
        var filter = criteria.Id(iid);
        var results = ExamineManager
            .Instance.SearchProviderCollection["InternalSearcher"]
            .Search(filter.Compile());
        if (results.Any())
        {
            return new MediaValues(results.First());
        }

        var media = umbraco.library.GetMedia(iid, false);
        if (media != null && media.Current != null)
        {
            media.MoveNext();
            return new MediaValues(media.Current);
        }

        return new MediaValues();
    }

    public static string GetImagePath(IPublishedContent node, string value)
    {
        return GetMedia(GetStringValue(node, value)).NiceUrl;
    }

    public static string Linkify(string text, string target = "_blank")
    {
        Regex domainRegex = new Regex(@"(((?<scheme>http(s)?):\/\/)?([\w-]+?\.\w+)+([a-zA-Z0-9\~\!\@\#\$\%\^\&amp;\*\(\)_\-\=\+\\\/\?\.\:\;\,]*)?)", RegexOptions.Compiled | RegexOptions.Multiline);

        return domainRegex.Replace(
            text,
            match =>
            {
                var link = match.ToString();
                var scheme = match.Groups["scheme"].Value == "https" ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;

                var url = new UriBuilder(link) { Scheme = scheme }.Uri.ToString();

                return string.Format(@"<a href=""{0}"" target=""{1}"">{2}</a>", url, target, link);
            }
        );
    }

    public static string Twitterfy(string rawTweet)
    {
        Regex screenName = new Regex(@"@\w+");
        Regex hashTag = new Regex(@"#\w+");

        string formattedTweet = screenName.Replace(rawTweet, delegate(Match m)
        {
            string val = m.Value.Trim('@');
            return string.Format("@<a target='_blank' href='http://twitter.com/{0}'>{1}</a>", val, val);
        });

        formattedTweet = hashTag.Replace(formattedTweet, delegate(Match m)
        {
            string val = m.Value;
            return string.Format("<a target='_blank' href='http://twitter.com/#search?q=%23{0}'>{1}</a>", val, val);
        });

        return formattedTweet;
    }

    public static string GetUrlPickerTitle(IPublishedContent node, string value)
    {
        if (node.GetProperty(value) != null)
        {
            return new RazorLibraryCore(null).ToDynamicXml((string)node.GetProperty(value).Value).XPath("//link-title").InnerText;
        }

        return "";
    }

    public static string GetUrlPickerUrl(IPublishedContent node, string value)
    {
        if (node.GetProperty(value) != null)
        {
            return new RazorLibraryCore(null).ToDynamicXml((string)node.GetProperty(value).Value).url;
        }

        return "";
    }

    public static string GetDynamicXmlFieldFromString(string xml, string value)
    {
        return new RazorLibraryCore(null).ToDynamicXml(xml).XPath("//" + value).InnerText;
    }

    public static string GetStringValueRecursive(IPublishedContent node, string propertyName)
    {
        var property = node.GetProperty(propertyName, true);
        if (property == null) return "";
        return property.Value.ToString();
    }

    public static string GetStringValue(IPublishedContent node, string propertyName)
    {
        if (node.HasValue(propertyName))
        {
            var property = node.GetProperty(propertyName);
            if (property == null) return "";

            return property.Value.ToString();
        }

        return "";
    }

    public static int GetIntValue(IPublishedContent node, string propertyName)
    {
        if (node.HasValue(propertyName))
        {
            var property = node.GetProperty(propertyName);
            if (property == null) return 0;

            return Convert.ToInt32(property.Value);
        }

        return 0;
    }


    public static string GetUrlFromLinkCSV(string csv)
    {
        var split = csv.Split(',');
        if (split.Length == 5)
        {
            return split[3];
        }
        return "";
    }

    public static string GetUrlFromContentPicker(IPublishedContent node, string propertyName)
    {
        if (node.HasValue(propertyName))
        {
            var n = new Node(Convert.ToInt32(GetStringValue(node, propertyName)));
            return n.Url;
        }

        return "";
    }

    public static string GetTitleFromLinkCSV(string csv)
    {
        var split = csv.Split(',');
        if (split.Length == 5)
        {
            return split[4];
        }
        return "";
    }

    public static Dictionary<int, object> GetPrevalues(int dataTypeId)
    {
        XPathNodeIterator preValueRootElementIterator = umbraco.library.GetPreValues(dataTypeId);
        preValueRootElementIterator.MoveNext(); //move to first 
        XPathNodeIterator preValueIterator = preValueRootElementIterator.Current.SelectChildren("preValue", "");
        var retVal = new Dictionary<int, object>();

        while (preValueIterator.MoveNext())
        {
            retVal.Add(Convert.ToInt32(preValueIterator.Current.GetAttribute("id", "")), preValueIterator.Current.Value);
        }
        return retVal;
    }

    public static List<Node> GetMNTPNodes(IPublishedContent node, string property)
    {

        var typedPublishedMNTPNodeListCSV = GetStringValue(node, property).Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);
        List<Node> nodes = new List<Node>();
        foreach (var item in typedPublishedMNTPNodeListCSV)
        {
            nodes.Add(new Node(item));
        }
        return nodes;
    }

    public static List<String> GetImagePaths(IPublishedContent node, string property)
    {
        var typedPublishedMNTPNodeListCSV = GetStringValue(node, property).Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);
        List<String> nodes = new List<String>();
        foreach (var item in typedPublishedMNTPNodeListCSV)
        {
            nodes.Add(GetMedia(item.ToString()).NiceUrl);
        }
        return nodes;
    }

    public static List<int> GetRelatedNews(int umbracoNodeId, string matchProperty)
    {
        return GetRelated(umbracoNodeId, matchProperty, "NewsArticle");
    }

    public static List<int> GetRelatedCaseStudies(int umbracoNodeId, string matchProperty)
    {
        return GetRelated(umbracoNodeId, matchProperty, "CaseStudyModulePage");
    }

    public static List<int> GetRelated(int umbracoNodeId, string matchProperty, string nta)
    {
        var root = new Node(umbracoNodeId);
        var results = new List<int>();

        if (root != null)
        {
            string dta = root.NodeTypeAlias;
            var searcher = ExamineManager.Instance.SearchProviderCollection["RelatedContentSearcher"];
            var searchCriteria = searcher.CreateSearchCriteria(Examine.SearchCriteria.BooleanOperation.Or);

            string luceneString = "(__NodeTypeAlias: " + nta + " AND parentID: " + umbracoNodeId + ") ";

            var matchPropertyValue = root.GetProperty(matchProperty);
            if (matchPropertyValue != null)
            {
                luceneString = luceneString + " AND " + GetLuceneQuery(matchProperty, matchPropertyValue.Value);
            }

            if (!String.IsNullOrEmpty(luceneString))
            {
                var query = searchCriteria.RawQuery(luceneString);
                var searchResults = searcher.Search(query).ToList().OrderByDescending((sr => (sr.Fields.ContainsKey("publicationDate") ? sr.Fields["publicationDate"] : "")));

                foreach (var sr in searchResults)
                {
                    //weird date parsing stuff because examine seems to change the format it saves dates in for NO BLOODY REASON.
                    var dt = DateTime.MinValue;

                    if (sr.Fields.ContainsKey("publicationDate"))
                    {
                        if (!DateTime.TryParseExact(sr.Fields["publicationDate"].Substring(0, 8), "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
                        {
                            DateTime.TryParse(sr.Fields["publicationDate"], out dt);
                        }
                    }

                    if (GetFieldValue(sr, "id") != umbracoNodeId.ToString() && results.Count() < 6)
                    {
                        results.Add(Convert.ToInt32(GetFieldValue(sr, "id")));
                    }
                }
            }
        }

        return results;
    }


    private static string GetLuceneQuery(string service, string ids)
    {
        if (!String.IsNullOrEmpty(ids))
        {
            string ret = "(";
            foreach (string id in ids.Split(','))
            {
                ret += service + @": """ + id + @"""" + " OR ";
            }
            ret = ret.TrimEnd((" OR ").ToCharArray());
            ret += ")";

            return ret;
        }
        return "";
    }

    private static string GetFieldValue(SearchResult sr, string field)
    {
        if (sr.Fields.ContainsKey(field))
        {
            return sr.Fields[field];
        }
        return "";
    }
}
