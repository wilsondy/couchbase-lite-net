﻿//
//  DocumentMethods.cs
//
//  Author:
//  	Jim Borden  <jim.borden@couchbase.com>
//
//  Copyright (c) 2015 Couchbase, Inc All rights reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

using Couchbase.Lite.Internal;
using Couchbase.Lite.Support;

#if NET_3_5
using Rackspace.Threading;
#endif

namespace Couchbase.Lite.Listener
{
    /// <summary>
    /// Methods that create, read, update and delete documents within a database.
    /// </summary>
    internal static class DocumentMethods
    {

        #region Constants

        private const string TAG = "DocumentMethods";

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns document by the specified docid from the specified db. Unless you request a 
        /// specific revision, the latest revision of the document will always be returned.
        /// </summary>
        /// <returns>The response state for further HTTP processing</returns>
        /// <param name="context">The context of the Couchbase Lite HTTP request</param>
        /// <remarks>
        /// http://docs.couchdb.org/en/latest/api/document/common.html#get--db-docid
        /// <remarks>
        public static ICouchbaseResponseState GetDocument(ICouchbaseListenerContext context)
        {
            return DatabaseMethods.PerformLogicWithDatabase(context, true, db => {
                var response = context.CreateResponse();
                string docId = context.DocumentName;
                bool isLocalDoc = docId.StartsWith("_local");

                DocumentContentOptions options = context.ContentOptions;
                string openRevsParam = context.GetQueryParam("open_revs");
                bool mustSendJson = context.ExplicitlyAcceptsType("application/json");
                if (openRevsParam == null || isLocalDoc) {
                    //Regular GET:
                    string revId = context.GetQueryParam("rev"); //often null
                    RevisionInternal rev;
                    bool includeAttachments = false, sendMultipart = false;
                    if (isLocalDoc) {
                        rev = db.Storage.GetLocalDocument(docId, revId);
                    } else {
                        includeAttachments = options.HasFlag(DocumentContentOptions.IncludeAttachments);
                        if(includeAttachments) {
                            sendMultipart = !mustSendJson;
                            options &= ~DocumentContentOptions.IncludeAttachments;
                        }

                        Status status = new Status();
                        rev = db.GetDocument(docId, revId, true, status);
                        if(rev != null) {
                            rev = ApplyOptions(options, rev, context, db, status);
                        }

                        if(rev == null) {
                            if(status.Code == StatusCode.Deleted) {
                                response.StatusReason = "deleted";
                            } else {
                                response.StatusReason = "missing";
                            }

                            response.InternalStatus = status.Code;
                            return response;
                        }
                    }

                    if(rev == null) {
                        response.InternalStatus = StatusCode.NotFound;
                        return response;
                    }

                    if(context.CacheWithEtag(rev.GetRevId())) {
                        response.InternalStatus = StatusCode.NotModified;
                        return response;
                    }

                    if(!isLocalDoc && includeAttachments) {
                        int minRevPos = 1;
                        IList<string> attsSince = context.GetJsonQueryParam("atts_since").AsList<string>();
                        string ancestorId = db.Storage.FindCommonAncestor(rev, attsSince);
                        if(ancestorId != null) {
                            minRevPos = RevisionInternal.GenerationFromRevID(ancestorId) + 1;
                        }

                        Status status = new Status();
                        bool attEncodingInfo = context.GetQueryParam<bool>("att_encoding_info", bool.TryParse, false);
                        if(!db.ExpandAttachments(rev, minRevPos, sendMultipart, attEncodingInfo, status)) {
                            response.InternalStatus = status.Code;
                            return response;
                        }
                    }

                    if(sendMultipart) {
                        response.MultipartWriter = MultipartWriterForRev(db, rev, "multipart/related");
                    } else {
                        response.JsonBody = rev.GetBody();
                    }
                } else {
                    // open_revs query:
                    IList<IDictionary<string, object>> result;
                    if(openRevsParam.Equals("all")) {
                        // ?open_revs=all returns all current/leaf revisions:
                        bool includeDeleted = context.GetQueryParam<bool>("include_deleted", bool.TryParse, false);
                        RevisionList allRevs = db.Storage.GetAllDocumentRevisions(docId, true);

                        result = new List<IDictionary<string, object>>();
                        foreach(var rev in allRevs) {
                            if(!includeDeleted && rev.IsDeleted()) {
                                continue;
                            }

                            Status status = new Status();
                            RevisionInternal loadedRev = db.RevisionByLoadingBody(rev, status);
                            if(loadedRev != null) {
                                ApplyOptions(options, loadedRev, context, db, status);
                            }

                            if(loadedRev != null) {
                                result.Add(new Dictionary<string, object> { { "ok", loadedRev.GetProperties() } });
                            } else if(status.Code <= StatusCode.InternalServerError) {
                                result.Add(new Dictionary<string, object> { { "missing", rev.GetRevId() } });
                            } else {
                                response.InternalStatus = status.Code;
                                return response;
                            }
                        }
                    } else {
                        // ?open_revs=[...] returns an array of specific revisions of the document:
                        var openRevs = context.GetJsonQueryParam("open_revs").AsList<object>();
                        if(openRevs == null) {
                            response.InternalStatus = StatusCode.BadParam;
                            return response;
                        }

                        result = new List<IDictionary<string, object>>();
                        foreach(var revIDObj in openRevs) {
                            var revID = revIDObj as string;
                            if(revID == null) {
                                response.InternalStatus = StatusCode.BadId;
                                return response;
                            }

                            Status status = new Status();
                            var rev = db.GetDocument(docId, revID, true, status);
                            if(rev != null) {
                                rev = ApplyOptions(options, rev, context, db, status);
                            }

                            if(rev != null) {
                                result.Add(new Dictionary<string, object>{ { "ok", rev.GetProperties() } });
                            } else {
                                result.Add(new Dictionary<string, object>{ { "missing", revID } });
                            }
                        }
                    }

                    if(mustSendJson) {
                        response["Content-Type"] = "application/json";
                        response.JsonBody = new Body(result.Cast<object>().ToList());
                    } else {
                        response.SetMultipartBody(result.Cast<object>().ToList(), "multipart/mixed");
                    }
                }

                return response;
            }).AsDefaultState();
        }

        /// <summary>
        /// Creates a new named document, or creates a new revision of the existing document.
        /// </summary>
        /// <returns>The response state for further HTTP processing</returns>
        /// <param name="context">The context of the Couchbase Lite HTTP request</param>
        /// <remarks>
        /// http://docs.couchdb.org/en/latest/api/document/common.html#put--db-docid
        /// <remarks>
        public static ICouchbaseResponseState UpdateDocument(ICouchbaseListenerContext context)
        {
            return PerformLogicWithDocumentBody(context, (db, body) =>
            {
                var response = context.CreateResponse();
                string docId = context.DocumentName;
                if(context.GetQueryParam<bool>("new_edits", bool.TryParse, true)) {
                    // Regular PUT:
                    return UpdateDb(context, db, docId, body, false);
                } else {
                    // PUT with new_edits=false -- forcible insertion of existing revision:
                    RevisionInternal rev = new RevisionInternal(body);
                    if(rev == null) {
                        response.InternalStatus = StatusCode.BadJson;
                        return response;
                    }

                    if(!docId.Equals(rev.GetDocId()) || rev.GetRevId() == null) {
                        response.InternalStatus = StatusCode.BadId;
                        return response;
                    }

                    var history = Database.ParseCouchDBRevisionHistory(body.GetProperties());
                    Status status = new Status();
                    try {
                      db.ForceInsert(rev, history, null);
                    } catch(CouchbaseLiteException e) {
                        status = e.CBLStatus;
                    }

                    if(!status.IsError) {
                        response.JsonBody = new Body(new Dictionary<string, object> {
                            { "ok", true },
                            { "id", rev.GetDocId() },
                            { "rev", rev.GetRevId() }
                        });
                    }

                    response.InternalStatus = status.Code;
                    return response;
                }
            }).AsDefaultState();
        }

        /// <summary>
        /// Creates a new document in the specified database, using the supplied JSON document structure.
        /// </summary>
        /// <returns>The response state for further HTTP processing</returns>
        /// <param name="context">The context of the Couchbase Lite HTTP request</param>
        /// <remarks>
        /// http://docs.couchdb.org/en/latest/api/database/common.html#post--db
        /// <remarks>
        public static ICouchbaseResponseState CreateDocument(ICouchbaseListenerContext context)
        {
            return PerformLogicWithDocumentBody(context, (db, body) => UpdateDb(context, db, null, body, false))
                .AsDefaultState();
        }

        /// <summary>
        /// Attempt to update a document based on the information in the HTTP request
        /// </summary>
        /// <returns>The resulting status of the operation</returns>
        /// <param name="context">The request context</param>
        /// <param name="db">The database in which the document exists</param>
        /// <param name="docId">The ID of the document being updated</param>
        /// <param name="body">The new document body</param>
        /// <param name="deleting">Whether or not the document is being deleted</param>
        /// <param name="allowConflict">Whether or not to allow a conflict to be inserted</param>
        /// <param name="outRev">The resulting revision of the document</param>
        public static StatusCode UpdateDocument(ICouchbaseListenerContext context, Database db, string docId, Body body, bool deleting, 
            bool allowConflict, out RevisionInternal outRev)
        {
            outRev = null;
            if (body != null && !body.IsValidJSON()) {
                return StatusCode.BadJson;
            }

            string prevRevId;
            if (!deleting) {
                var properties = body.GetProperties();
                deleting = properties.GetCast<bool>("_deleted");
                if (docId == null) {
                    // POST's doc ID may come from the _id field of the JSON body.
                    docId = properties.GetCast<string>("_id");
                    if (docId == null && deleting) {
                        return StatusCode.BadId;
                    }
                }

                // PUT's revision ID comes from the JSON body.
                prevRevId = properties.GetCast<string>("_rev");
            } else {
                // DELETE's revision ID comes from the ?rev= query param
                prevRevId = context.GetQueryParam("rev");
            }

            // A backup source of revision ID is an If-Match header:
            if (prevRevId == null) {
                prevRevId = context.IfMatch();
            }

            if (docId == null && deleting) {
                return StatusCode.BadId;
            }

            RevisionInternal rev = new RevisionInternal(docId, null, deleting);
            rev.SetBody(body);

            StatusCode status = StatusCode.Created;
            try {
                if (docId != null && docId.StartsWith("_local")) {
                    outRev = db.Storage.PutLocalRevision(rev, prevRevId, true); //TODO: Doesn't match iOS
                } else {
                    Status retStatus = new Status();
                    outRev = db.PutRevision(rev, prevRevId, allowConflict, retStatus);
                    status = retStatus.Code;
                }
            } catch(CouchbaseLiteException e) {
                status = e.Code;
            }

            return status;
        }

        /// <summary>
        /// Marks the specified document as deleted by adding a field _deleted with the value true. 
        /// Documents with this field will not be returned within requests anymore, but stay in the database.
        /// </summary>
        /// <returns>The response state for further HTTP processing</returns>
        /// <param name="context">The context of the Couchbase Lite HTTP request</param>
        /// <remarks>
        /// http://docs.couchdb.org/en/latest/api/document/common.html#delete--db-docid
        /// <remarks>
        public static ICouchbaseResponseState DeleteDocument(ICouchbaseListenerContext context)
        {
            return DatabaseMethods.PerformLogicWithDatabase(context, true, db =>
            {
                string docId = context.DocumentName;
                return UpdateDb(context, db, docId, null, true);
            }).AsDefaultState();
        }

        /// <summary>
        /// Returns the file attachment associated with the document. The raw data of the associated attachment is returned 
        /// (just as if you were accessing a static file. The returned Content-Type will be the same as the content type 
        /// set when the document attachment was submitted into the database.
        /// </summary>
        /// <returns>The response state for further HTTP processing</returns>
        /// <param name="context">The context of the Couchbase Lite HTTP request</param>
        /// <remarks>
        /// http://docs.couchdb.org/en/latest/api/document/attachments.html#get--db-docid-attname
        /// <remarks>
        public static ICouchbaseResponseState GetAttachment(ICouchbaseListenerContext context)
        {
            return DatabaseMethods.PerformLogicWithDatabase(context, true, db =>
            {
                Status status = new Status();
                var rev = db.GetDocument(context.DocumentName, context.GetQueryParam("rev"), false, 
                    status);
                    
                if(rev ==null) {
                    return context.CreateResponse(status.Code);
                }
                if(context.CacheWithEtag(rev.GetRevId())) {
                    return context.CreateResponse(StatusCode.NotModified);
                }

                string acceptEncoding = context.RequestHeaders["Accept-Encoding"];
                bool acceptEncoded = acceptEncoding != null && acceptEncoding.Contains("gzip") &&
                    context.RequestHeaders["Range"] == null;

                var attachment = db.GetAttachmentForRevision(rev, context.AttachmentName, status);
                if(attachment == null) {
                    return context.CreateResponse(status.Code);
                }

                var response = context.CreateResponse();
                if(context.Method.Equals(HttpMethod.Head)) {
                    var length = attachment.Length;
                    if(acceptEncoded && attachment.Encoding == AttachmentEncoding.GZIP &&
                        attachment.EncodedLength > 0) {
                        length = attachment.EncodedLength;
                    }

                    response["Content-Length"] = length.ToString();
                } else {
                    var contents = acceptEncoded ? attachment.EncodedContent : attachment.Content;
                    if(contents == null) {
                        response.InternalStatus = StatusCode.NotFound;
                        return response;
                    }

                    response.BinaryBody = contents;
                }

                response["Content-Type"] = attachment.ContentType;
                if(acceptEncoded && attachment.Encoding == AttachmentEncoding.GZIP) {
                    response["Content-Encoding"] = "gzip";
                }

                return response;
            }).AsDefaultState();
        }

        /// <summary>
        /// Uploads the supplied content as an attachment to the specified document.
        /// </summary>
        /// <returns>The response state for further HTTP processing</returns>
        /// <param name="context">The context of the Couchbase Lite HTTP request</param>
        /// <remarks>
        /// http://docs.couchdb.org/en/latest/api/document/attachments.html#put--db-docid-attname
        /// <remarks>
        public static ICouchbaseResponseState UpdateAttachment(ICouchbaseListenerContext context)
        {
            var state = new AsyncOpCouchbaseResponseState();
            DatabaseMethods.PerformLogicWithDatabase(context, true, db =>
            {
                
                var blob = db.AttachmentWriter;
                var httpBody = new byte[context.ContentLength];
                context.BodyStream.ReadAsync(httpBody, 0, httpBody.Length).ContinueWith(t => {
                    if(t.Result == 0) {
                        state.Response = context.CreateResponse(StatusCode.BadAttachment);
                        state.SignalFinished();
                        return;
                    }

                    blob.AppendData(httpBody);
                    blob.Finish();

                    state.Response = UpdateAttachment(context, db, context.AttachmentName, context.DocumentName, blob);
                    state.SignalFinished();
                });

                return null;
            });
                
            return state;
        }

        /// <summary>
        /// Deletes the attachment of the specified doc.
        /// </summary>
        /// <returns>The response state for further HTTP processing</returns>
        /// <param name="context">The context of the Couchbase Lite HTTP request</param>
        /// <remarks>
        /// http://docs.couchdb.org/en/latest/api/document/attachments.html#delete--db-docid-attname
        /// <remarks>
        public static ICouchbaseResponseState DeleteAttachment(ICouchbaseListenerContext context)
        {
            return DatabaseMethods.PerformLogicWithDatabase(context, true, db =>
            UpdateAttachment(context, db, context.AttachmentName, context.DocumentName, null)).AsDefaultState();
        }

        #endregion

        #region Private Methods

        private static MultipartWriter MultipartWriterForRev(Database db, RevisionInternal rev, string contentType)
        {
            MultipartWriter writer = new MultipartWriter(contentType, null);
            writer.AddData(rev.GetJson());
            var attachments = rev.GetAttachments();
            foreach (var attachmentName in attachments.Keys) {
                var attachment = attachments[attachmentName].AsDictionary<string, object>();
                if (attachment.GetCast<bool>("follows")) {
                    var disposition = String.Format("attachment; filename={0}", Misc.QuoteString(attachmentName));
                    writer.SetNextPartHeaders(new Dictionary<string, string> {
                        { "Content-Disposition", disposition }
                    });

                    var status = new Status();
                    var attachmentObj = db.AttachmentForDict(attachment, attachmentName, status);
                    if (attachmentObj == null) {
                        return null;
                    }

                    var fileUri = attachmentObj.ContentUrl;
                    if (fileUri != null) {
                        writer.AddFileUrl(fileUri);
                    } else {
                        writer.AddStream(attachmentObj.ContentStream);
                    }
                }
            }

            return writer;
        }

        // Factors out the logic of opening the database and reading the document body from the HTTP request
        // and performs the specified logic on the body received in the request, barring any problems
        private static CouchbaseLiteResponse PerformLogicWithDocumentBody(ICouchbaseListenerContext context, 
            Func<Database, Body, CouchbaseLiteResponse> callback)
        {
            return DatabaseMethods.PerformLogicWithDatabase(context, true, db =>
            {
                MultipartDocumentReader reader = new MultipartDocumentReader(db);
                reader.SetContentType(context.RequestHeaders["Content-Type"]);
                reader.AppendData(context.BodyStream.ReadAllBytes());
                try {
                    reader.Finish();
                } catch(InvalidOperationException) {
                    return context.CreateResponse(StatusCode.BadRequest);
                }

                return callback(db, new Body(reader.GetDocumentProperties()));
            });
        }

        // Apply the options in the URL query to the specified revision and create a new revision object
        internal static RevisionInternal ApplyOptions(DocumentContentOptions options, RevisionInternal rev, ICouchbaseListenerContext context,
            Database db, Status outStatus)
        {
            if ((options & (DocumentContentOptions.IncludeRevs | DocumentContentOptions.IncludeRevsInfo | DocumentContentOptions.IncludeConflicts |
                DocumentContentOptions.IncludeAttachments | DocumentContentOptions.IncludeLocalSeq)) != 0) {
                var dst = rev.GetProperties(); 
                if (options.HasFlag(DocumentContentOptions.IncludeLocalSeq)) {
                    dst["_local_seq"] = rev.GetSequence();
                }

                if (options.HasFlag(DocumentContentOptions.IncludeRevs)) {
                    dst["_revisions"] = db.Storage.GetRevisionHistory(rev, null);
                }

                if (options.HasFlag(DocumentContentOptions.IncludeRevsInfo)) {
                    dst["_revs_info"] = db.Storage.GetRevisionHistory(rev).Select(x =>
                    {
                        string status = "available";
                        if(x.IsDeleted()) {
                            status = "deleted";
                        } else if(x.IsMissing()) {
                            status = "missing";
                        }

                        return new Dictionary<string, object> {
                            { "rev", x.GetRevId() },
                            { "status", status }
                        };
                    });
                }

                if (options.HasFlag(DocumentContentOptions.IncludeConflicts)) {
                    RevisionList revs = db.Storage.GetAllDocumentRevisions(rev.GetDocId(), true);
                    if (revs.Count > 1) {
                        dst["_conflicts"] = revs.Select(x =>
                        {
                            return x.Equals(rev) || x.IsDeleted() ? null : x.GetRevId();
                        });
                    }
                }

                RevisionInternal nuRev = new RevisionInternal(dst);
                if (options.HasFlag(DocumentContentOptions.IncludeAttachments)) {
                    bool attEncodingInfo = context != null && context.GetQueryParam<bool>("att_encoding_info", bool.TryParse, false);
                    if(!db.ExpandAttachments(nuRev, 0, false, !attEncodingInfo, outStatus)) {
                        return null;
                    }
                }

                rev = nuRev;
            }

            return rev;
        }

        // Perform a document operation on the specified database
        private static CouchbaseLiteResponse UpdateDb(ICouchbaseListenerContext context, Database db, string docId, Body body, bool deleting)
        {
            var response = context.CreateResponse();
            if (docId != null) {
                // On PUT/DELETE, get revision ID from either ?rev= query, If-Match: header, or doc body:
                string revParam = context.GetQueryParam("rev");
                string ifMatch = context.RequestHeaders["If-Match"];
                if (ifMatch != null) {
                    if (revParam == null) {
                        revParam = ifMatch;
                    } else if (!revParam.Equals(ifMatch)) {
                        return context.CreateResponse(StatusCode.BadRequest);
                    }
                }

                if (revParam != null && body != null) {
                    var revProp = body.GetPropertyForKey("_rev");
                    if (revProp == null) {
                        // No _rev property in body, so use ?rev= query param instead:
                        var props = body.GetProperties();
                        props["_rev"] = revParam;
                        body = new Body(props);
                    } else if (!revProp.Equals(revParam)) {
                        return context.CreateResponse(StatusCode.BadRequest); // mismatch between _rev and rev
                    }
                }
            }

            RevisionInternal rev;
            StatusCode status = UpdateDocument(context, db, docId, body, deleting, false, out rev);
            if ((int)status < 300) {
                context.CacheWithEtag(rev.GetRevId()); // set ETag
                if (!deleting) {
                    var url = context.RequestUrl;
                    if (docId != null) {
                        response["Location"] = url.AbsoluteUri;
                    }
                }

                response.JsonBody = new Body(new Dictionary<string, object> {
                    { "ok", true },
                    { "id", rev.GetDocId() },
                    { "rev", rev.GetRevId() }
                });
            }

            response.InternalStatus = status;
            return response;
        }
           
        // Update the given attachment using the provided info
        private static CouchbaseLiteResponse UpdateAttachment(ICouchbaseListenerContext context, Database db, 
            string attachment, string docId, BlobStoreWriter body)
        {
            RevisionInternal rev = db.UpdateAttachment(attachment, body, context.RequestHeaders["Content-Type"], AttachmentEncoding.None,
                    docId, context.GetQueryParam("rev") ?? context.IfMatch());

            var response = context.CreateResponse();
            response.JsonBody = new Body(new Dictionary<string, object> {
                { "ok", true },
                { "id", rev.GetDocId() },
                { "rev", rev.GetRevId() }
            });
            context.CacheWithEtag(rev.GetRevId());
            if (body != null) {
                response["Location"] = context.RequestUrl.AbsoluteUri;
            }

            return response;
        }

        #endregion
    }
}

