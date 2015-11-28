/* ***** BEGIN LICENSE BLOCK *****
 * Version: MPL 1.1/GPL 2.0/LGPL 2.1
 *
 * The contents of this file are subject to the Mozilla Public License Version
 * 1.1 (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 * http://www.mozilla.org/MPL/
 *
 * Software distributed under the License is distributed on an "AS IS" basis,
 * WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License
 * for the specific language governing rights and limitations under the
 * License.
 *
 * The Original Code is Mozilla Universal charset detector code.
 *
 * The Initial Developer of the Original Code is
 * Netscape Communications Corporation.
 * Portions created by the Initial Developer are Copyright (C) 2001
 * the Initial Developer. All Rights Reserved.
 *
 * Contributor(s):
 *          Shy Shalom <shooshX@gmail.com>
 *          Rudi Pettazzi <rudi.pettazzi@gmail.com> (C# port)
 * 
 * Alternatively, the contents of this file may be used under the terms of
 * either the GNU General Public License Version 2 or later (the "GPL"), or
 * the GNU Lesser General Public License Version 2.1 or later (the "LGPL"),
 * in which case the provisions of the GPL or the LGPL are applicable instead
 * of those above. If you wish to allow use of your version of this file only
 * under the terms of either the GPL or the LGPL, and not to allow others to
 * use your version of this file under the terms of the MPL, indicate your
 * decision by deleting the provisions above and replace them with the notice
 * and other provisions required by the GPL or the LGPL. If you do not delete
 * the provisions above, a recipient may use your version of this file under
 * the terms of any one of the MPL, the GPL or the LGPL.
 *
 * ***** END LICENSE BLOCK ***** */

using System;

namespace Ude.Core
{
    /// <summary>
    /// TODO remove protected fields
    /// </summary>
    public abstract class UniversalDetector
    {


        protected const float SHORTCUT_THRESHOLD = 0.95f;
        protected const float MINIMUM_THRESHOLD = 0.20f;

        internal InputState inputState;

        /// <summary>
        /// Start of the file
        /// </summary>
        protected bool start;

        /// <summary>
        /// De byte array has data?
        /// </summary>
        protected bool gotData;

        /// <summary>
        /// Most of the time true of <see cref="detectedCharset"/> is set. TODO not always
        /// </summary>
        protected bool done;

        /// <summary>
        /// Lastchar, but not always filled. TODO remove?
        /// </summary>
        protected byte lastChar;

        /// <summary>
        /// best guess rate between 0 and 1 (inluding)
        /// </summary>
        protected int bestGuess;

        /// <summary>
        /// tries
        /// </summary>
        protected const int PROBERS_NUM = 3;

        /// <summary>
        /// "list" of probers
        /// </summary>
        protected CharsetProber[] charsetProbers = new CharsetProber[PROBERS_NUM];


        /// <summary>
        /// TODO unknown
        /// </summary>
        protected CharsetProber escCharsetProber;

        /// <summary>
        /// Detected charset. Most of the time <see cref="done"/> is true
        /// </summary>
        protected string detectedCharset;

        protected UniversalDetector()
        {
            this.start = true;
            this.inputState = InputState.PureASCII;
            this.lastChar = 0x00;
            this.bestGuess = -1;
        }

        public virtual void Feed(byte[] buf, int offset, int len)
        {
            if (done)
            {
                return;
            }

            len = Math.Min(len, buf.Length);

            if (len > 0)
                gotData = true;

            // If the data starts with BOM, we know it is UTF
            if (start)
            {
                var bomSet = FindCharSetByBom(buf, len);
                if (bomSet != null)
                {
                    detectedCharset = bomSet;
                    done = true;
                    return;
                }
            }

            for (int i = 0; i < len; i++)
            {

                // other than 0xa0, if every other character is ascii, the page is ascii
                if ((buf[i] & 0x80) != 0 && buf[i] != 0xA0)
                {
                    // we got a non-ascii byte (high-byte)
                    if (inputState != InputState.Highbyte)
                    {
                        inputState = InputState.Highbyte;

                        // kill EscCharsetProber if it is active
                        if (escCharsetProber != null)
                        {
                            escCharsetProber = null;
                        }

                        // start multibyte and singlebyte charset prober
                        if (charsetProbers[0] == null)
                            charsetProbers[0] = new MBCSGroupProber();
                        if (charsetProbers[1] == null)
                            charsetProbers[1] = new SBCSGroupProber();
                        if (charsetProbers[2] == null)
                            charsetProbers[2] = new Latin1Prober();
                    }
                }
                else
                {
                    if (inputState == InputState.PureASCII &&
                        (buf[i] == 0x1B || (buf[i] == 0x7B && lastChar == 0x7E)))
                    {
                        // found escape character or HZ "~{"
                        inputState = InputState.EscASCII;
                    }
                    lastChar = buf[i];
                }
            }

            ProbingState st;

            switch (inputState)
            {
                case InputState.EscASCII:
                    if (escCharsetProber == null)
                    {
                        escCharsetProber = new EscCharsetProber();
                    }
                    st = escCharsetProber.HandleData(buf, offset, len);
                    if (st == ProbingState.FoundIt)
                    {
                        done = true;
                        detectedCharset = escCharsetProber.GetCharsetName();
                    }
                    break;
                case InputState.Highbyte:
                    for (int i = 0; i < PROBERS_NUM; i++)
                    {
                        if (charsetProbers[i] != null)
                        {
                            st = charsetProbers[i].HandleData(buf, offset, len);
#if DEBUG
                            charsetProbers[i].DumpStatus();
#endif
                            if (st == ProbingState.FoundIt)
                            {
                                done = true;
                                detectedCharset = charsetProbers[i].GetCharsetName();
                                return;
                            }
                        }
                    }
                    break;
                    // else pure ascii
            }
        }

        private string FindCharSetByBom(byte[] buf, int len)
        {
            string bomSet = null;
            start = false;
            if (len > 3)
            {
                switch (buf[0])
                {
                    case 0xEF:
                        if (0xBB == buf[1] && 0xBF == buf[2])
                            bomSet = "UTF-8";
                        break;
                    case 0xFE:
                        if (0xFF == buf[1] && 0x00 == buf[2] && 0x00 == buf[3])
                            // FE FF 00 00  UCS-4, unusual octet order BOM (3412)
                            bomSet = "X-ISO-10646-UCS-4-3412";
                        else if (0xFF == buf[1])
                            bomSet = "UTF-16BE";
                        break;
                    case 0x00:
                        if (0x00 == buf[1] && 0xFE == buf[2] && 0xFF == buf[3])
                            bomSet = "UTF-32BE";
                        else if (0x00 == buf[1] && 0xFF == buf[2] && 0xFE == buf[3])
                            // 00 00 FF FE  UCS-4, unusual octet order BOM (2143)
                            bomSet = "X-ISO-10646-UCS-4-2143";
                        break;
                    case 0xFF:
                        if (0xFE == buf[1] && 0x00 == buf[2] && 0x00 == buf[3])
                            bomSet = "UTF-32LE";
                        else if (0xFE == buf[1])
                            bomSet = "UTF-16LE";
                        break;
                } // switch
            }
            return bomSet;
        }

        /// <summary>
        /// Notify detector that no further data is available. 
        /// </summary>
        public virtual void DataEnd()
        {
            if (!gotData)
            {
                // we haven't got any data yet, return immediately 
                // caller program sometimes call DataEnd before anything has 
                // been sent to detector
                return;
            }

            if (detectedCharset != null)
            {
                done = true;
                Report(detectedCharset, 1.0f, null);
                return;
            }

            if (inputState == InputState.Highbyte)
            {
                float proberConfidence = 0.0f;
                float maxProberConfidence = 0.0f;
                CharsetProber maxProber = null;
                for (int i = 0; i < PROBERS_NUM; i++)
                {
                    if (charsetProbers[i] != null)
                    {
                        proberConfidence = charsetProbers[i].GetConfidence();
                        if (proberConfidence > maxProberConfidence)
                        {
                            maxProberConfidence = proberConfidence;
                            maxProber = charsetProbers[i];
                        }
                    }
                }
                //TODO why done isn't true?
                if (maxProberConfidence > MINIMUM_THRESHOLD)
                {
                    Report(maxProber);
                }

            }
            else if (inputState == InputState.PureASCII)
            {
                //TODO why done isn't true?
                Report("ASCII", 1.0f, null);
            }
        }

        private DetectionSummary summary;

        protected void Report(string charset, float confidence, CharsetProber prober)
        {
            summary = new DetectionSummary(new DetectionResult(charset, confidence, prober, null));

        }


        protected void Report(CharsetProber prober)
        {
            summary = new DetectionSummary(new DetectionResult(prober.GetCharsetName(), prober.GetConfidence(), prober, null));

        }

        public string Charset
        {
            get { return summary.Detected.Charset; }
        }

        public float Confidence
        {
            get { return summary.Detected.Confidence; }
        }


    }
}
