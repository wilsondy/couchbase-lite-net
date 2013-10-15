/**
 * Couchbase Lite for .NET
 *
 * Original iOS version by Jens Alfke
 * Android Port by Marty Schoch, Traun Leyden
 * C# Port by Zack Gramana
 *
 * Copyright (c) 2012, 2013 Couchbase, Inc. All rights reserved.
 * Portions (c) 2013 Xamarin, Inc. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file
 * except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the
 * License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND,
 * either express or implied. See the License for the specific language governing permissions
 * and limitations under the License.
 */

using Couchbase;
using Couchbase.Internal;
using Sharpen;

namespace Couchbase.Internal
{
	/// <summary>A simple container for attachment metadata.</summary>
	/// <remarks>A simple container for attachment metadata.</remarks>
	public class CBLAttachmentInternal
	{
		public enum CBLAttachmentEncoding
		{
			CBLAttachmentEncodingNone,
			CBLAttachmentEncodingGZIP
		}

		private string name;

		private string contentType;

		private CBLBlobKey blobKey;

		private long length;

		private long encodedLength;

		private CBLAttachmentInternal.CBLAttachmentEncoding encoding;

		private int revpos;

		public CBLAttachmentInternal(string name, string contentType)
		{
			this.name = name;
			this.contentType = contentType;
		}

		public virtual bool IsValid()
		{
			if (encoding != CBLAttachmentInternal.CBLAttachmentEncoding.CBLAttachmentEncodingNone)
			{
				if (encodedLength == 0 && length > 0)
				{
					return false;
				}
			}
			else
			{
				if (encodedLength > 0)
				{
					return false;
				}
			}
			if (revpos == 0)
			{
				return false;
			}
			return true;
		}

		public virtual string GetName()
		{
			return name;
		}

		public virtual string GetContentType()
		{
			return contentType;
		}

		public virtual CBLBlobKey GetBlobKey()
		{
			return blobKey;
		}

		public virtual void SetBlobKey(CBLBlobKey blobKey)
		{
			this.blobKey = blobKey;
		}

		public virtual long GetLength()
		{
			return length;
		}

		public virtual void SetLength(long length)
		{
			this.length = length;
		}

		public virtual long GetEncodedLength()
		{
			return encodedLength;
		}

		public virtual void SetEncodedLength(long encodedLength)
		{
			this.encodedLength = encodedLength;
		}

		public virtual CBLAttachmentInternal.CBLAttachmentEncoding GetEncoding()
		{
			return encoding;
		}

		public virtual void SetEncoding(CBLAttachmentInternal.CBLAttachmentEncoding encoding
			)
		{
			this.encoding = encoding;
		}

		public virtual int GetRevpos()
		{
			return revpos;
		}

		public virtual void SetRevpos(int revpos)
		{
			this.revpos = revpos;
		}
	}
}
