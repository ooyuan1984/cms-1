﻿using System;
using System.Collections.Specialized;
using System.Web.UI.WebControls;
using SiteServer.Abstractions;
using SiteServer.BackgroundPages.Controls;
using SiteServer.BackgroundPages.Core;
using SiteServer.CMS.Context;
using SiteServer.CMS.Repositories;

namespace SiteServer.BackgroundPages.Cms
{
	public class PageContentTags : BasePageCms
    {
		public Repeater RptContents;
        public SqlPager SpContents;

		public Button BtnAddTag;

        public static string GetRedirectUrl(int siteId)
        {
            return PageUtils.GetCmsUrl(siteId, nameof(PageContentTags), null);
        }

        public void Page_Load(object sender, EventArgs e)
        {
            if (IsForbidden) return;

            if (AuthRequest.IsQueryExists("Delete"))
            {
                var tagName = AuthRequest.GetQueryString("TagName");

                try
                {
                    var contentIdList = DataProvider.ContentTagRepository.GetContentIdListByTagAsync(tagName, SiteId).GetAwaiter().GetResult();
                    if (contentIdList.Count > 0)
                    {
                        foreach (var contentId in contentIdList)
                        {
                            var tags = DataProvider.ContentRepository.GetValueAsync(Site.TableName, contentId, ContentAttribute.Tags).GetAwaiter().GetResult();
                            if (!string.IsNullOrEmpty(tags))
                            {
                                var contentTagList = StringUtils.GetStringList(tags);
                                contentTagList.Remove(tagName);
                                DataProvider.ContentRepository.UpdateAsync(Site.TableName, contentId, ContentAttribute.Tags, TranslateUtils.ObjectCollectionToString(contentTagList)).GetAwaiter().GetResult();
                            }
                        }
                    }
                    DataProvider.ContentTagRepository.DeleteTagAsync(tagName, SiteId).GetAwaiter().GetResult();
                    AuthRequest.AddSiteLogAsync(SiteId, "删除内容标签", $"内容标签:{tagName}").GetAwaiter().GetResult();
                    SuccessDeleteMessage();
                }
                catch (Exception ex)
                {
                    FailDeleteMessage(ex);
                }
            }

            SpContents.ControlToPaginate = RptContents;
            SpContents.ItemsPerPage = Site.PageSize;

            SpContents.SelectCommand = DataProvider.ContentTagRepository.GetSqlString(SiteId, 0, true, 0);
            SpContents.SortField = nameof(ContentTag.UseNum);
            SpContents.SortMode = SortMode.DESC;

            RptContents.ItemDataBound += RptContents_ItemDataBound;

            if (IsPostBack) return;

            VerifySitePermissions(Constants.WebSitePermissions.Configuration);

            SpContents.DataBind();

            var showPopWinString = ModalContentTagAdd.GetOpenWindowStringToAdd(SiteId);
            BtnAddTag.Attributes.Add("onClick", showPopWinString);
        }

        private void RptContents_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            if (e.Item.ItemType != ListItemType.Item && e.Item.ItemType != ListItemType.AlternatingItem) return;

            var tag = SqlUtils.EvalString(e.Item.DataItem, nameof(ContentTag.Tag));
            var level = SqlUtils.EvalInt(e.Item.DataItem, nameof(ContentTag.Level));
            var useNum = SqlUtils.EvalInt(e.Item.DataItem, nameof(ContentTag.UseNum));

            var ltlTagName = (Literal)e.Item.FindControl("ltlTagName");
            var ltlCount = (Literal)e.Item.FindControl("ltlCount");
            var ltlContents = (Literal)e.Item.FindControl("ltlContents");
            var ltlEditUrl = (Literal)e.Item.FindControl("ltlEditUrl");
            var ltlDeleteUrl = (Literal)e.Item.FindControl("ltlDeleteUrl");

            var cssClass = "tag_popularity_1";
            if (level == 2)
            {
                cssClass = "tag_popularity_2";
            }
            else if (level == 3)
            {
                cssClass = "tag_popularity_3";
            }

            ltlTagName.Text = $@"<span class=""{cssClass}"">{tag}</span>";
            ltlCount.Text = useNum.ToString();

            ltlContents.Text = $@"<a href=""{PageContentsTag.GetRedirectUrl(SiteId, tag)}"">查看内容</a>";

            var showPopWinString = ModalContentTagAdd.GetOpenWindowStringToEdit(SiteId, tag);
            ltlEditUrl.Text = $"<a href=\"javascript:;\" onClick=\"{showPopWinString}\">编辑</a>";

            var urlDelete = PageUtils.GetCmsUrl(SiteId, nameof(PageContentTags), new NameValueCollection
            {
                {"TagName", tag},
                {"Delete", true.ToString()}
            });
            ltlDeleteUrl.Text =
                $"<a href=\"{urlDelete}\" onClick=\"javascript:return confirm('此操作将删除内容标签“{tag}”，确认吗？');\">删除</a>";
        }
	}
}
