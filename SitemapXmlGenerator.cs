using Sitecore;
using Sitecore.ContentSearch.Linq.Utilities;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Layouts;
using Sitecore.Links;
using Sitecore.Pipelines.HttpRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Caching;
using System.Xml;
using System.Linq.Expressions;
using Sitecore.ContentSearch.Linq;
using PerstorpCom.Search.Sublayouts;
using Sitecore.Resources.Media;
using System.IO;
using System.Text;


namespace SitecoreFromArg.SimpleSitemapXml
{
    public sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding { get { return Encoding.UTF8; } }
    }

    public class SitemapXmlGenerator : HttpRequestProcessor
    {
        public string sitemapUrl { get; set; }
        public string excludedPaths { get; set; }
        public string cacheTime { get; set; }

        public override void Process(HttpRequestArgs args)
        {
            Assert.ArgumentNotNull((object)args, "args");
            if (Context.Site == null || string.IsNullOrEmpty(Context.Site.RootPath.Trim())) return;
            if (Context.Page.FilePath.Length > 0) return;

            if (!args.Url.FilePath.Contains(sitemapUrl)) return;

            // Important to return qualified XML (text/xml) for sitemaps
            args.Context.Response.ClearHeaders();
            args.Context.Response.ClearContent();
            args.Context.Response.ContentType = "text/xml";

            // Checking the cache first
            var sitemapXmlCache = args.Context.Cache["sitemapxml"];
            if (sitemapXmlCache != null)
            {
                args.Context.Response.Write(sitemapXmlCache.ToString());
                args.Context.Response.End();

                return;
            }

            //
            var options = LinkManager.GetDefaultUrlOptions();
            options.AlwaysIncludeServerUrl = true;

            // Creating the XML Header
            var sw = new Utf8StringWriter();
            //var xml = new XmlTextWriter(args.Context.Response.Output);

            var xml = new XmlTextWriter(sw);
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            // Creating the XML Body
            try
            {
                string anonymousUser = @"extranet\Anonymous";
                Sitecore.Security.Accounts.User scUser = Sitecore.Security.Accounts.User.FromName(anonymousUser, false);

                using (new Sitecore.Security.Accounts.UserSwitcher(scUser))
                {
                    var items = Context.Database.SelectItems("fast:" + Context.Site.RootPath + "//*");

                    foreach (var item in items)
                    {
                        if (IsPage(item))
                        {
                            if (!item.Paths.IsContentItem) continue;
                            if (excludedPaths.Split('|').Any(p => item.Paths.ContentPath.Contains(p))) continue;
                            xml.WriteStartElement("url");
                            xml.WriteElementString("loc", LinkManager.GetItemUrl(item, options));
                            xml.WriteElementString("lastmod", item.Statistics.Updated.ToString("yyyy-MM-ddThh:mm:sszzz"));
                            xml.WriteEndElement();
                        }
                    }

                    var siteIndex = Sitecore.ContentSearch.ContentSearchManager.GetIndex("sitecore_web_index");

                    using (var siteSearchContext = siteIndex.CreateSearchContext())
                    {
                        var filePredicate = PredicateBuilder.True<SearchResultItem>();
                        filePredicate = filePredicate.And(p => p.TemplateName == "Pdf");
                        var fileResult = siteSearchContext.GetQueryable<SearchResultItem>().Where(filePredicate).GetResults();
                        //MediaUrlOptions muo = new MediaUrlOptions();
                        //muo.AlwaysIncludeServerUrl = true;

                        foreach (var file in fileResult)
                        {
                            //Get item to only index files that can be viewed/downloaded by anonymous user
                            var item = (MediaItem)file.Document.GetItem();

                            if (item != null)
                            {
                                if (excludedPaths.Split('|').Any(p => file.Document.Url.Contains(p))) continue;
                                xml.WriteStartElement("url");
                                xml.WriteElementString("loc", Globals.ServerUrl + file.Document.Url);
                                xml.WriteElementString("lastmod", file.Document.Updated.ToString("yyyy-MM-ddThh:mm:sszzz"));
                                xml.WriteEndElement();
                            }
                            //MediaManager.GetMediaUrl((MediaItem)item, murl);
                            //var titleField = file.Document.GetField("title");
                        }
                    }
                }
                args.Context.Response.Write(sw.ToString());

            }
            finally
            {
                xml.WriteEndElement();
                xml.WriteEndDocument();
                xml.Flush();

                // Cache XML content
                args.Context.Cache.Add("sitemapxml", sw.ToString(), null,
                              DateTime.Now.AddSeconds(int.Parse(cacheTime)),
                              Cache.NoSlidingExpiration,
                              CacheItemPriority.Normal,
                              null);

                args.Context.Response.Flush();
                //args.Context.Response.SuppressContent = true;
                //args.Context.ApplicationInstance.CompleteRequest();
                args.Context.Response.End();
            }
        }

        /// <summary>
        /// Identify the items with a presentation detail
        /// </summary>
        /// <param name="item">Item to check</param>
        /// <returns></returns>
        private bool IsPage(Item item)
        {
            var result = false;
            var layoutField = new LayoutField(item.Fields[FieldIDs.LayoutField]);
            if (!layoutField.InnerField.HasValue || string.IsNullOrEmpty(layoutField.Value)) return false;
            var layout = LayoutDefinition.Parse(layoutField.Value);
            foreach (var deviceObj in layout.Devices)
            {
                var device = deviceObj as DeviceDefinition;
                if (device == null) return false;
                if (device.Renderings.Count > 0)
                {
                    result = true;
                }
            }
            return result;
        }
    }
}
