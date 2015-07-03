//
// Extensions.cs
//
// Author:
//  Zachary Gramana  <zack@xamarin.com>
//
// Copyright (c) 2013, 2014 Xamarin Inc (http://www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
/*
* Original iOS version by Jens Alfke
* Ported to Android by Marty Schoch, Traun Leyden
*
* Copyright (c) 2012, 2013, 2014 Couchbase, Inc. All rights reserved.
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
using System.Reflection;
using System.Net;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Specialized;

namespace Sharpen
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.RegularExpressions;

    internal static class Extensions
    {
        private static readonly long EPOCH_TICKS;

        static Extensions ()
        {
            DateTime time = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            EPOCH_TICKS = time.Ticks;
        }

        public static StringBuilder AppendRange (this StringBuilder sb, string str, int start, int end)
        {
            return sb.Append (str, start, end - start);
        }

        
        static UTF8Encoding UTF8Encoder = new UTF8Encoding (false, true);
        public static Encoding GetEncoding (string name)
        {
//          Encoding e = Encoding.GetEncoding (name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            try {
                Encoding e = Encoding.GetEncoding (name.Replace ('_','-'));
                if (e is UTF8Encoding)
                    return UTF8Encoder;
                return e;
            } catch (ArgumentException) {
                throw new UnsupportedCharsetException (name);
            }
        }

        public static U Get<T, U> (this IDictionary<T, U> d, T key)
        {
            U val = default(U);
            d.TryGetValue (key, out val);
            return val;
        }

        public static U Put<T, U> (this IDictionary<T, U> d, T key, U value)
        {
            U old;
            d.TryGetValue (key, out old);
            d [key] = value;
            return old;
        }
        
        public static void PutAll<T, U> (this IDictionary<T, U> d, IDictionary<T, U> values)
        {
            foreach (KeyValuePair<T,U> val in values)
                d[val.Key] = val.Value;
        }

        public static Stream GetResourceAsStream (this Type type, string name)
        {
            var manifestResourceStream = type.Assembly.GetManifestResourceStream (name);
            if (manifestResourceStream == null) {
                return null;
            }

            return InputStream.Wrap (manifestResourceStream);
        }
            
        public static DateTime CreateDate (long milliSecondsSinceEpoch)
        {
            long num = EPOCH_TICKS + (milliSecondsSinceEpoch * 10000);
            return new DateTime (num);
        }

        public static DateTimeOffset MillisToDateTimeOffset (long milliSecondsSinceEpoch, long offsetMinutes)
        {
            TimeSpan offset = TimeSpan.FromMinutes ((double)offsetMinutes);
            long num = EPOCH_TICKS + (milliSecondsSinceEpoch * 10000);
            return new DateTimeOffset (num + offset.Ticks, offset);
        }

        public static T Remove<T> (this IList<T> list, int i)
        {
            T old;
            try {
                old = list[i];
                list.RemoveAt (i);
            } catch (IndexOutOfRangeException) {
                throw new NoSuchElementException ();
            }
            return old;
        }

        public static string ReplaceAll (this string str, string regex, string replacement)
        {
            Regex rgx = new Regex (regex);
            
            if (replacement.IndexOfAny (new char[] { '\\','$' }) != -1) {
                // Back references not yet supported
                StringBuilder sb = new StringBuilder ();
                for (int n=0; n<replacement.Length; n++) {
                    char c = replacement [n];
                    if (c == '$')
                        throw new NotSupportedException ("Back references not supported");
                    if (c == '\\')
                        c = replacement [++n];
                    sb.Append (c);
                }
                replacement = sb.ToString ();
            }
            
            return rgx.Replace (str, replacement);
        }
        
        public static bool RegionMatches (this string str, bool ignoreCase, int toOffset, string other, int ooffset, int len)
        {
            if (toOffset < 0 || ooffset < 0 || toOffset + len > str.Length || ooffset + len > other.Length)
                return false;
            return string.Compare (str, toOffset, other, ooffset, len) == 0;
        }

        public static T Set<T> (this IList<T> list, int index, T item)
        {
            T old = list[index];
            list[index] = item;
            return old;
        }

        public static void RemoveAll<T,U> (this ICollection<T> col, ICollection<U> items) where U:T
        {
            foreach (var u in items)
                col.Remove (u);
        }

        public static void Sort<T> (this IList<T> list, IComparer<T> comparer)
        {
            List<T> sorted = new List<T> (list);
            sorted.Sort (comparer);
            for (int i = 0; i < list.Count; i++) {
                list[i] = sorted[i];
            }
        }

        public static IList<T> SubList<T> (this IList<T> list, int start, int len)
        {
            List<T> sublist = new List<T> (len);
            for (int i = start; i < (start + len); i++) {
                sublist.Add (list[i]);
            }
            return sublist;
        }

        public static long ToMillisecondsSinceEpoch (this DateTime dateTime)
        {
            if (dateTime.Kind != DateTimeKind.Utc) {
                throw new ArgumentException ("dateTime is expected to be expressed as a UTC DateTime", "dateTime");
            }
            return new DateTimeOffset (DateTime.SpecifyKind (dateTime, DateTimeKind.Utc), TimeSpan.Zero).ToMillisecondsSinceEpoch ();
        }

        public static long ToMillisecondsSinceEpoch (this DateTimeOffset dateTimeOffset)
        {
            return (((dateTimeOffset.Ticks - dateTimeOffset.Offset.Ticks) - EPOCH_TICKS) / TimeSpan.TicksPerMillisecond);
        }

        public static string ToString (int val, int bas)
        {
            return Convert.ToString (val, bas);
        }
    }
}
