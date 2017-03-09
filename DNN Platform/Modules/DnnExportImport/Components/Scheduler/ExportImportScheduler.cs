﻿#region Copyright
// 
// DotNetNuke® - http://www.dnnsoftware.com
// Copyright (c) 2002-2017
// by DotNetNuke Corporation
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or substantial portions 
// of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED 
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using Dnn.ExportImport.Components.Common;
using Dnn.ExportImport.Components.Controllers;
using Dnn.ExportImport.Components.Engines;
using Dnn.ExportImport.Components.Models;
using DotNetNuke.Instrumentation;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.Scheduling;

namespace Dnn.ExportImport.Components.Scheduler
{
    /// <summary>
    /// Implements a SchedulerClient for the Exporting/Importing of site items.
    /// </summary>
    public class ExportImportScheduler : SchedulerClient
    {
        private static readonly ILog Logger = LoggerSource.Instance.GetLogger(typeof(ExportImportScheduler));

        public ExportImportScheduler(ScheduleHistoryItem objScheduleHistoryItem)
        {
            ScheduleHistoryItem = objScheduleHistoryItem;
        }

        public override void DoWork()
        {
            try
            {
                var job = EntitiesController.Instance.GetFirstActiveJob();
                if (job != null)
                {
                    var lastSuccessFulDateTime = GetLastSuccessfulExportDateTime(ScheduleHistoryItem.ScheduleID);
                    Logger.Trace("Export/Import: Starting. Start time " + lastSuccessFulDateTime.ToString("g"));
                    ScheduleHistoryItem.AddLogNote($"Starting. Start time <b>{lastSuccessFulDateTime:g}</b>");
                    ExportImportResult result;
                    var engine = new ExportImportEngine();
                    switch (job.JobType)
                    {
                        case JobType.Export:
                            result = engine.Export(job, ScheduleHistoryItem);
                            break;
                        case JobType.Import:
                            result = engine.Import(job, ScheduleHistoryItem);
                            break;
                        default:
                            throw new Exception("Unknown job type: " + job.JobType);
                    }

                    if (result != null)
                    {
                        ScheduleHistoryItem.Succeeded = true;
                        EntitiesController.Instance.UpdateJobStatus(job);
                        var sb = new StringBuilder();
                        sb.Append(job.JobType == JobType.Export
                            ? "<br/><b>EXPORT Successful</b>"
                            : "<br/><b>IMPORT Successful</b>");
                        sb.Append("<br/>Status: " + job.JobStatus);
                        if (result.Summary.Count > 0)
                        {
                            sb.Append("<br/><b>Summary:</b><br/><ul>");
                            foreach (var entry in result.Summary)
                            {
                                sb.Append($"<li>{entry.Key}: {entry.Value}</li>");
                            }
                            sb.Append("</ul>");
                        }

                        ScheduleHistoryItem.AddLogNote(sb.ToString());
                    }

                    Logger.Trace("Export/Import: Job Completed");
                }
                else
                {
                    ScheduleHistoryItem.Succeeded = true;
                    ScheduleHistoryItem.AddLogNote("<br/>No Export/Import jobs queued for processing.");
                }
                SetLastSuccessfulIndexingDateTime(ScheduleHistoryItem.ScheduleID, ScheduleHistoryItem.StartDate);
            }
            catch (Exception ex)
            {
                ScheduleHistoryItem.Succeeded = false;
                ScheduleHistoryItem.AddLogNote("<br/>Export/Import EXCEPTION: " + ex.Message);
                Errored(ref ex);
                if (ScheduleHistoryItem.ScheduleSource != ScheduleSource.STARTED_FROM_BEGIN_REQUEST)
                {
                    Exceptions.LogException(ex);
                }
            }
        }

        private static DateTime FixSqlDateTime(DateTime datim)
        {
            if (datim <= SqlDateTime.MinValue.Value)
                datim = SqlDateTime.MinValue.Value.AddDays(1);
            else if (datim >= SqlDateTime.MaxValue.Value)
                datim = SqlDateTime.MaxValue.Value.AddDays(-1);
            return datim;
        }

        private static DateTimeOffset GetLastSuccessfulExportDateTime(int scheduleId)
        {
            var settings = SchedulingProvider.Instance().GetScheduleItemSettings(scheduleId);
            var lastValue = settings[Constants.LastJobSuccessDate] as string;

            DateTime lastTime;
            if (!string.IsNullOrEmpty(lastValue) &&
                DateTime.TryParseExact(lastValue, Constants.JobRunDateTimeFormat, null, DateTimeStyles.None, out lastTime))
            {
                // retrieves the date as UTC but returns to caller as local
                lastTime = FixSqlDateTime(lastTime).ToLocalTime().ToLocalTime();
                if (lastTime > DateTime.Now) lastTime = DateTime.Now;
            }
            else
            {
                lastTime = SqlDateTime.MinValue.Value.AddDays(1);
            }

            return lastTime;
        }

        private static void SetLastSuccessfulIndexingDateTime(int scheduleId, DateTime startDateLocal)
        {
            SchedulingProvider.Instance().AddScheduleItemSetting(scheduleId, Constants.LastJobSuccessDate, startDateLocal.ToUniversalTime().ToString(Constants.JobRunDateTimeFormat));
        }
    }
}