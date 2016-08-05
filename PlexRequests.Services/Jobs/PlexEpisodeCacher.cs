﻿#region Copyright
// /************************************************************************
//    Copyright (c) 2016 Jamie Rees
//    File: PlexEpisodeCacher.cs
//    Created By: Jamie Rees
//   
//    Permission is hereby granted, free of charge, to any person obtaining
//    a copy of this software and associated documentation files (the
//    "Software"), to deal in the Software without restriction, including
//    without limitation the rights to use, copy, modify, merge, publish,
//    distribute, sublicense, and/or sell copies of the Software, and to
//    permit persons to whom the Software is furnished to do so, subject to
//    the following conditions:
//   
//    The above copyright notice and this permission notice shall be
//    included in all copies or substantial portions of the Software.
//   
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
//    LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
//    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
//    WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  ************************************************************************/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;

using NLog;

using PlexRequests.Api.Interfaces;
using PlexRequests.Api.Models.Plex;
using PlexRequests.Core;
using PlexRequests.Core.SettingModels;
using PlexRequests.Helpers;
using PlexRequests.Services.Interfaces;
using PlexRequests.Services.Models;

using Quartz;

namespace PlexRequests.Services.Jobs
{
    public class PlexEpisodeCacher : IJob
    {
        public PlexEpisodeCacher(ISettingsService<PlexSettings> plexSettings, IPlexApi plex, ICacheProvider cache,
            IJobRecord rec)
        {
            Plex = plexSettings;
            PlexApi = plex;
            Cache = cache;
            Job = rec;
        }

        private ISettingsService<PlexSettings> Plex { get; }
        private static Logger Log = LogManager.GetCurrentClassLogger();
        private IPlexApi PlexApi { get; }
        private ICacheProvider Cache { get; }
        private IJobRecord Job { get; }
        private const int ResultCount = 25;
        private const string PlexType = "episode";


        public void CacheEpisodes()
        {
            var videoHashset = new HashSet<Video>();
            var settings = Plex.GetSettings();
            if (string.IsNullOrEmpty(settings.PlexAuthToken))
            {
                return;
            }

            var sections = PlexApi.GetLibrarySections(settings.PlexAuthToken, settings.FullUri);
            var tvSection = sections.Directories.FirstOrDefault(x => x.type.Equals(PlexMediaType.Show.ToString(), StringComparison.CurrentCultureIgnoreCase));
            var tvSectionId = tvSection?.Key;

            var currentPosition = 0;
            int totalSize;

            var episodes = PlexApi.GetAllEpisodes(settings.PlexAuthToken, settings.FullUri, tvSectionId, currentPosition, ResultCount);
            int.TryParse(episodes.TotalSize, out totalSize);

            currentPosition += ResultCount;
            while (currentPosition < totalSize)
            {
                videoHashset.UnionWith(PlexApi.GetAllEpisodes(settings.PlexAuthToken, settings.FullUri, tvSectionId, currentPosition, ResultCount).Video
                    .Where(x => x.Type.Equals(PlexType, StringComparison.InvariantCultureIgnoreCase)));
                currentPosition += ResultCount;
            }

            var episodesModel = new HashSet<PlexEpisodeModel>();

            foreach (var video in videoHashset)
            {
                var ratingKey = video.RatingKey;
                var metadata = PlexApi.GetEpisodeMetaData(settings.PlexAuthToken, settings.FullUri, ratingKey);

                foreach (var metadataVideo in metadata.Video)
                {
                    episodesModel.Add(new PlexEpisodeModel
                    {
                        RatingKey = metadataVideo.RatingKey,
                        EpisodeTitle = metadataVideo.Title,
                        Guid = metadataVideo.Guid,
                        ShowTitle = metadataVideo.GrandparentTitle
                    });
                }
            }


            if (episodesModel.Any())
            {
                Cache.Set(CacheKeys.PlexEpisodes, episodesModel, CacheKeys.TimeFrameMinutes.SchedulerCaching);
            }
        }

        public void Execute(IJobExecutionContext context)
        {
            try
            {
                CacheEpisodes();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
            finally
            {
                Job.Record(JobNames.EpisodeCacher);
            }
        }
    }
}