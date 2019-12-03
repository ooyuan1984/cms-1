﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web.UI.WebControls;
using SiteServer.Abstractions;
using SiteServer.CMS.Context;
using SiteServer.CMS.Core;

namespace SiteServer.BackgroundPages.Cms
{
	public class PageTemplateAssets : BasePageCms
	{
	    public Literal LtlPageTitle;
        public Repeater RptContents;
	    public Button BtnConfig;
        public Button BtnAdd;

        private string _type;
        private string _name;
	    private string _ext;
	    private string _assetsDir;
        private string _directoryPath;

        public const string TypeInclude = "include";
        public const string TypeJs = "js";
        public const string TypeCss= "css";
        public const string NameInclude = "包含文件";
        public const string NameJs = "脚本文件";
        public const string NameCss = "样式文件";
	    public const string ExtInclude = ".html";
	    public const string ExtJs = ".js";
	    public const string ExtCss = ".css";

        public static string GetRedirectUrl(int siteId, string type)
        {
            return PageUtils.GetCmsUrl(siteId, nameof(PageTemplateAssets), new NameValueCollection
            {
                {"type", type}
            });
        }

        public void Page_Load(object sender, EventArgs e)
        {
            if (IsForbidden) return;

            PageUtils.CheckRequestParameter("siteId", "type");
            _type = AuthRequest.GetQueryString("type");
            var tips = string.Empty;

            if (_type == TypeInclude)
            {
                _name = NameInclude;
                _ext = ExtInclude;
                _assetsDir = Site.TemplatesAssetsIncludeDir.Trim('/');
                
                tips = $@"包含文件存放在 <code>{_assetsDir}</code> 目录中，模板中使用 &lt;stl:include file=""/{_assetsDir}/包含文件.html""&gt;&lt;/stl:include&gt; 引用。";
            }
            else if (_type == TypeJs)
            {
                _name = NameJs;
                _ext = ExtJs;
                _assetsDir = Site.TemplatesAssetsJsDir.Trim('/');
                tips =
                    $@"脚本文件存放在 <code>{_assetsDir}</code> 目录中，模板中使用 &lt;script type=""text/javascript"" src=""{{stl.siteUrl}}/{_assetsDir}/脚本文件.js""&gt;&lt;/script&gt; 引用。";
            }
            else if (_type == TypeCss)
            {
                _name = NameCss;
                _ext = ExtCss;
                _assetsDir = Site.TemplatesAssetsCssDir.Trim('/');
                tips = $@"样式文件存放在 <code>{_assetsDir}</code> 目录中，模板中使用 &lt;link rel=""stylesheet"" type=""text/css"" href=""{{stl.siteUrl}}/{_assetsDir}/样式文件.css"" /&gt; 引用。";
            }

            if (string.IsNullOrEmpty(_assetsDir)) return;

            _directoryPath = PathUtility.MapPath(Site, "@/" + _assetsDir);

            if (AuthRequest.IsQueryExists("delete"))
            {
                var fileName = AuthRequest.GetQueryString("fileName");

                try
                {
                    FileUtils.DeleteFileIfExists(PathUtils.Combine(_directoryPath, fileName));
                    AuthRequest.AddSiteLogAsync(SiteId, $"删除{_name}", $"{_name}:{fileName}").GetAwaiter().GetResult();
                    SuccessDeleteMessage();
                }
                catch (Exception ex)
                {
                    FailDeleteMessage(ex);
                }
            }

            if (IsPostBack) return;

            VerifySitePermissions(Constants.WebSitePermissions.Template);

            LtlPageTitle.Text = $"{_name}管理";
            InfoMessage(tips);

            DirectoryUtils.CreateDirectoryIfNotExists(_directoryPath);
            var fileNames = DirectoryUtils.GetFileNames(_directoryPath);
            var fileNameList = new List<string>();
            foreach (var fileName in fileNames)
            {
                if (StringUtils.EqualsIgnoreCase(PathUtils.GetExtension(fileName), _ext))
                {
                    fileNameList.Add(fileName);
                }
            }

            RptContents.DataSource = fileNameList;
            RptContents.ItemDataBound += RptContents_ItemDataBound;
            RptContents.DataBind();

            BtnConfig.Attributes.Add("onClick", ModalTemplateAssetsConfig.GetOpenWindowString(SiteId, _type));
            BtnAdd.Attributes.Add("onClick", $"location.href='{PageTemplateAssetsAdd.GetRedirectUrlToAdd(SiteId, _type)}';return false");
        }

        private void RptContents_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            if (e.Item.ItemType != ListItemType.Item && e.Item.ItemType != ListItemType.AlternatingItem) return;

            var fileName = (string)e.Item.DataItem;

            var ltlFileName = (Literal)e.Item.FindControl("ltlFileName");
            var ltlView = (Literal)e.Item.FindControl("ltlView");
            var ltlEdit = (Literal)e.Item.FindControl("ltlEdit");
            var ltlDelete = (Literal)e.Item.FindControl("ltlDelete");

            ltlFileName.Text = fileName;

            ltlView.Text = $@"<a href=""{PageUtility.GetSiteUrl(Site, $"{_assetsDir}/{fileName}", true)}"" target=""_blank"">查看</a>";
            ltlEdit.Text =
                $@"<a href=""{PageTemplateAssetsAdd.GetRedirectUrlToEdit(SiteId, _type, fileName)}"">编辑</a>";
            ltlDelete.Text =
                $@"<a href=""javascript:;"" onClick=""{AlertUtils.ConfirmDelete($"删除{_name}", $"此操作将删除{_name}，确认吗", $"{GetRedirectUrl(SiteId, _type)}&delete={true}&fileName={fileName}")}"">删除</a>";
        }
	}
}
