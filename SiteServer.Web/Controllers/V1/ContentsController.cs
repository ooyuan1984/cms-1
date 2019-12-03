﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using NSwag.Annotations;
using SiteServer.Abstractions;
using SiteServer.CMS.Api.V1;
using SiteServer.CMS.Core;
using SiteServer.CMS.Core.Create;
using SiteServer.CMS.DataCache;
using SiteServer.CMS.Plugin;
using SiteServer.CMS.Repositories;

namespace SiteServer.API.Controllers.V1
{
    [RoutePrefix("v1/contents")]
    public class ContentsController : ApiController
    {
        private const string RouteSite = "{siteId:int}";
        private const string RouteChannel = "{siteId:int}/{channelId:int}";
        private const string RouteContent = "{siteId:int}/{channelId:int}/{id:int}";

        [HttpPost, Route(RouteChannel)]
        public async Task<IHttpActionResult> Create(int siteId, int channelId)
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                var sourceId = request.GetPostInt(ContentAttribute.SourceId.ToCamelCase());
                bool isAuth;
                if (sourceId == SourceManager.User)
                {
                    isAuth = request.IsUserLoggin && await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId, Constants.ChannelPermissions.ContentAdd);
                }
                else
                {
                    isAuth = request.IsApiAuthenticated && await
                                 DataProvider.AccessTokenRepository.IsScopeAsync(request.ApiToken, Constants.ScopeContents) ||
                             request.IsUserLoggin &&
                             await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentAdd) ||
                             request.IsAdminLoggin &&
                             await request.AdminPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentAdd);
                }
                if (!isAuth) return Unauthorized();

                var site = await DataProvider.SiteRepository.GetAsync(siteId);
                if (site == null) return BadRequest("无法确定内容对应的站点");

                var channelInfo = await ChannelManager.GetChannelAsync(siteId, channelId);
                if (channelInfo == null) return BadRequest("无法确定内容对应的栏目");

                if (!channelInfo.IsContentAddable) return BadRequest("此栏目不能添加内容");

                var attributes = request.GetPostObject<Dictionary<string, object>>();
                if (attributes == null) return BadRequest("无法从body中获取内容实体");
                var checkedLevel = request.GetPostInt("checkedLevel");

                var adminName = request.AdminName;

                var isChecked = checkedLevel >= site.CheckContentLevel;
                if (isChecked)
                {
                    if (sourceId == SourceManager.User || request.IsUserLoggin)
                    {
                        isChecked = await request.UserPermissionsImpl.HasChannelPermissionsAsync(siteId, channelId,
                            Constants.ChannelPermissions.ContentCheck);
                    }
                    else if (request.IsAdminLoggin)
                    {
                        isChecked = await request.AdminPermissionsImpl.HasChannelPermissionsAsync(siteId, channelId,
                            Constants.ChannelPermissions.ContentCheck);
                    }
                }

                var contentInfo = new Content(attributes)
                {
                    SiteId = siteId,
                    ChannelId = channelId,
                    AddUserName = adminName,
                    LastEditDate = DateTime.Now,
                    LastEditUserName = adminName,
                    AdminId = request.AdminId,
                    UserId = request.UserId,
                    SourceId = sourceId,
                    Checked = isChecked,
                    CheckedLevel = checkedLevel
                };

                contentInfo.Id = await DataProvider.ContentRepository.InsertAsync(site, channelInfo, contentInfo);

                foreach (var service in await PluginManager.GetServicesAsync())
                {
                    try
                    {
                        service.OnContentFormSubmit(new ContentFormSubmitEventArgs(siteId, channelId, contentInfo.Id, attributes, contentInfo));
                    }
                    catch (Exception ex)
                    {
                        await LogUtils.AddErrorLogAsync(service.PluginId, ex, nameof(IService.ContentFormSubmit));
                    }
                }

                if (contentInfo.Checked)
                {
                    await CreateManager.CreateContentAsync(siteId, channelId, contentInfo.Id);
                    await CreateManager.TriggerContentChangedEventAsync(siteId, channelId);
                }

                await request.AddSiteLogAsync(siteId, channelId, contentInfo.Id, "添加内容",
                    $"栏目:{await ChannelManager.GetChannelNameNavigationAsync(siteId, contentInfo.ChannelId)},内容标题:{contentInfo.Title}");

                return Ok(new
                {
                    Value = contentInfo
                });
            }
            catch (Exception ex)
            {
                await LogUtils.AddErrorLogAsync(ex);
                return InternalServerError(ex);
            }
        }

        [HttpPut, Route(RouteContent)]
        public async Task<IHttpActionResult> Update(int siteId, int channelId, int id)
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                var sourceId = request.GetPostInt(ContentAttribute.SourceId.ToCamelCase());
                bool isAuth;
                if (sourceId == SourceManager.User)
                {
                    isAuth = request.IsUserLoggin && await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId, Constants.ChannelPermissions.ContentEdit);
                }
                else
                {
                    isAuth = request.IsApiAuthenticated && await
                                 DataProvider.AccessTokenRepository.IsScopeAsync(request.ApiToken, Constants.ScopeContents) ||
                             request.IsUserLoggin &&
                             await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentEdit) ||
                             request.IsAdminLoggin &&
                             await request.AdminPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentEdit);
                }
                if (!isAuth) return Unauthorized();

                var site = await DataProvider.SiteRepository.GetAsync(siteId);
                if (site == null) return BadRequest("无法确定内容对应的站点");

                var channelInfo = await ChannelManager.GetChannelAsync(siteId, channelId);
                if (channelInfo == null) return BadRequest("无法确定内容对应的栏目");

                var attributes = request.GetPostObject<Dictionary<string, object>>();
                if (attributes == null) return BadRequest("无法从body中获取内容实体");

                var adminName = request.AdminName;

                var contentInfo = await DataProvider.ContentRepository.GetAsync(site, channelInfo, id);
                if (contentInfo == null) return NotFound();

                foreach (var attribute in attributes)
                {
                    contentInfo.Set(attribute.Key, attribute.Value);
                }

                contentInfo.SiteId = siteId;
                contentInfo.ChannelId = channelId;
                contentInfo.AddUserName = adminName;
                contentInfo.LastEditDate = DateTime.Now;
                contentInfo.LastEditUserName = adminName;
                contentInfo.SourceId = sourceId;

                var postCheckedLevel = request.GetPostInt(ContentAttribute.CheckedLevel.ToCamelCase());
                var isChecked = postCheckedLevel >= site.CheckContentLevel;
                var checkedLevel = postCheckedLevel;

                contentInfo.Checked = isChecked;
                contentInfo.CheckedLevel = checkedLevel;

                await DataProvider.ContentRepository.UpdateAsync(site, channelInfo, contentInfo);

                foreach (var service in await PluginManager.GetServicesAsync())
                {
                    try
                    {
                        service.OnContentFormSubmit(new ContentFormSubmitEventArgs(siteId, channelId, contentInfo.Id, attributes, contentInfo));
                    }
                    catch (Exception ex)
                    {
                        await LogUtils.AddErrorLogAsync(service.PluginId, ex, nameof(IService.ContentFormSubmit));
                    }
                }

                if (contentInfo.Checked)
                {
                    await CreateManager.CreateContentAsync(siteId, channelId, contentInfo.Id);
                    await CreateManager.TriggerContentChangedEventAsync(siteId, channelId);
                }

                await request.AddSiteLogAsync(siteId, channelId, contentInfo.Id, "修改内容",
                    $"栏目:{await ChannelManager.GetChannelNameNavigationAsync(siteId, contentInfo.ChannelId)},内容标题:{contentInfo.Title}");

                return Ok(new
                {
                    Value = contentInfo
                });
            }
            catch (Exception ex)
            {
                await LogUtils.AddErrorLogAsync(ex);
                return InternalServerError(ex);
            }
        }

        [HttpDelete, Route(RouteContent)]
        public async Task<IHttpActionResult> Delete(int siteId, int channelId, int id)
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                var sourceId = request.GetPostInt(ContentAttribute.SourceId.ToCamelCase());
                bool isAuth;
                if (sourceId == SourceManager.User)
                {
                    isAuth = request.IsUserLoggin && await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId, Constants.ChannelPermissions.ContentDelete);
                }
                else
                {
                    isAuth = request.IsApiAuthenticated && await
                                 DataProvider.AccessTokenRepository.IsScopeAsync(request.ApiToken, Constants.ScopeContents) ||
                             request.IsUserLoggin &&
                             await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentDelete) ||
                             request.IsAdminLoggin &&
                             await request.AdminPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentDelete);
                }
                if (!isAuth) return Unauthorized();

                var site = await DataProvider.SiteRepository.GetAsync(siteId);
                if (site == null) return BadRequest("无法确定内容对应的站点");

                var channelInfo = await ChannelManager.GetChannelAsync(siteId, channelId);
                if (channelInfo == null) return BadRequest("无法确定内容对应的栏目");

                if (!await request.AdminPermissionsImpl.HasChannelPermissionsAsync(siteId, channelId,
                    Constants.ChannelPermissions.ContentDelete)) return Unauthorized();

                var tableName = await ChannelManager.GetTableNameAsync(site, channelInfo);

                var contentInfo = await DataProvider.ContentRepository.GetAsync(site, channelInfo, id);
                if (contentInfo == null) return NotFound();

                await ContentUtility.DeleteAsync(tableName, site, channelId, id);

                //DataProvider.ContentRepository.DeleteContent(tableName, site, channelId, id);

                return Ok(new
                {
                    Value = contentInfo
                });
            }
            catch (Exception ex)
            {
                await LogUtils.AddErrorLogAsync(ex);
                return InternalServerError(ex);
            }
        }

        [HttpGet, Route(RouteContent)]
        public async Task<IHttpActionResult> Get(int siteId, int channelId, int id)
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                var sourceId = request.GetPostInt(ContentAttribute.SourceId.ToCamelCase());
                bool isAuth;
                if (sourceId == SourceManager.User)
                {
                    isAuth = request.IsUserLoggin && await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId, Constants.ChannelPermissions.ContentView);
                }
                else
                {
                    isAuth = request.IsApiAuthenticated && await
                                 DataProvider.AccessTokenRepository.IsScopeAsync(request.ApiToken, Constants.ScopeContents) ||
                             request.IsUserLoggin &&
                             await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentView) ||
                             request.IsAdminLoggin &&
                             await request.AdminPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentView);
                }
                if (!isAuth) return Unauthorized();

                var site = await DataProvider.SiteRepository.GetAsync(siteId);
                if (site == null) return BadRequest("无法确定内容对应的站点");

                var channelInfo = await ChannelManager.GetChannelAsync(siteId, channelId);
                if (channelInfo == null) return BadRequest("无法确定内容对应的栏目");

                if (!await request.AdminPermissionsImpl.HasChannelPermissionsAsync(siteId, channelId,
                    Constants.ChannelPermissions.ContentView)) return Unauthorized();

                var contentInfo = await DataProvider.ContentRepository.GetAsync(site, channelInfo, id);
                if (contentInfo == null) return NotFound();

                return Ok(new
                {
                    Value = contentInfo
                });
            }
            catch (Exception ex)
            {
                await LogUtils.AddErrorLogAsync(ex);
                return InternalServerError(ex);
            }
        }

        [OpenApiOperation("获取站点内容API", "")]
        [HttpGet, Route(RouteSite)]
        public async Task<IHttpActionResult> GetSiteContents(int siteId)
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                var sourceId = request.GetPostInt(ContentAttribute.SourceId.ToCamelCase());
                bool isAuth;
                if (sourceId == SourceManager.User)
                {
                    isAuth = request.IsUserLoggin && await request.UserPermissions.HasChannelPermissionsAsync(siteId, siteId, Constants.ChannelPermissions.ContentView);
                }
                else
                {
                    isAuth = request.IsApiAuthenticated && await
                                 DataProvider.AccessTokenRepository.IsScopeAsync(request.ApiToken, Constants.ScopeContents) ||
                             request.IsUserLoggin &&
                             await request.UserPermissions.HasChannelPermissionsAsync(siteId, siteId,
                                 Constants.ChannelPermissions.ContentView) ||
                             request.IsAdminLoggin &&
                             await request.AdminPermissions.HasChannelPermissionsAsync(siteId, siteId,
                                 Constants.ChannelPermissions.ContentView);
                }
                if (!isAuth) return Unauthorized();

                var site = await DataProvider.SiteRepository.GetAsync(siteId);
                if (site == null) return BadRequest("无法确定内容对应的站点");

                if (!await request.AdminPermissionsImpl.HasChannelPermissionsAsync(siteId, siteId,
                    Constants.ChannelPermissions.ContentView)) return Unauthorized();

                var tableName = site.TableName;

                var parameters = new ApiContentsParameters(request);

                var (tupleList, count) = await DataProvider.ContentRepository.ApiGetContentIdListBySiteIdAsync(tableName, siteId, parameters);
                var value = new List<IDictionary<string, object>>();
                foreach (var tuple in tupleList)
                {
                    var contentInfo = await DataProvider.ContentRepository.GetAsync(site, tuple.Item1, tuple.Item2);
                    if (contentInfo != null)
                    {
                        value.Add(contentInfo.ToDictionary());
                    }
                }

                return Ok(new PageResponse(value, parameters.Top, parameters.Skip, request.HttpRequest.Url.AbsoluteUri) {Count = count});
            }
            catch (Exception ex)
            {
                await LogUtils.AddErrorLogAsync(ex);
                return InternalServerError(ex);
            }
        }

        [HttpGet, Route(RouteChannel)]
        public async Task<IHttpActionResult> GetChannelContents(int siteId, int channelId)
        {
            try
            {
                var request = await AuthenticatedRequest.GetAuthAsync();
                var sourceId = request.GetPostInt(ContentAttribute.SourceId.ToCamelCase());
                bool isAuth;
                if (sourceId == SourceManager.User)
                {
                    isAuth = request.IsUserLoggin && await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId, Constants.ChannelPermissions.ContentView);
                }
                else
                {
                    isAuth = request.IsApiAuthenticated && await
                                 DataProvider.AccessTokenRepository.IsScopeAsync(request.ApiToken, Constants.ScopeContents) ||
                             request.IsUserLoggin &&
                             await request.UserPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentView) ||
                             request.IsAdminLoggin &&
                             await request.AdminPermissions.HasChannelPermissionsAsync(siteId, channelId,
                                 Constants.ChannelPermissions.ContentView);
                }
                if (!isAuth) return Unauthorized();

                var site = await DataProvider.SiteRepository.GetAsync(siteId);
                if (site == null) return BadRequest("无法确定内容对应的站点");

                var channelInfo = await ChannelManager.GetChannelAsync(siteId, channelId);
                if (channelInfo == null) return BadRequest("无法确定内容对应的栏目");

                if (!await request.AdminPermissionsImpl.HasChannelPermissionsAsync(siteId, channelId,
                    Constants.ChannelPermissions.ContentView)) return Unauthorized();

                var tableName = await ChannelManager.GetTableNameAsync(site, channelInfo);

                var top = request.GetQueryInt("top", 20);
                var skip = request.GetQueryInt("skip");
                var like = request.GetQueryString("like");
                var orderBy = request.GetQueryString("orderBy");

                var (list, count) = await DataProvider.ContentRepository.ApiGetContentIdListByChannelIdAsync(tableName, siteId, channelId, top, skip, like, orderBy, request.QueryString);
                var value = new List<IDictionary<string, object>>();
                foreach(var (contentChannelId, contentId) in list)
                {
                    var contentInfo = await DataProvider.ContentRepository.GetAsync(site, contentChannelId, contentId);
                    if (contentInfo != null)
                    {
                        value.Add(contentInfo.ToDictionary());
                    }
                }

                return Ok(new PageResponse(value, top, skip, request.HttpRequest.Url.AbsoluteUri) { Count = count });
            }
            catch (Exception ex)
            {
                await LogUtils.AddErrorLogAsync(ex);
                return InternalServerError(ex);
            }
        }
    }
}
