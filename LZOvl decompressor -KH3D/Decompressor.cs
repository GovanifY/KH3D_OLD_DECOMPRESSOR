using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LZOvl_decompressor__KH3D
{
    class Decompressor
    {
        public static long Decompress(System.IO.Stream instream, long inLength, System.IO.Stream outstream)
        {
            #region Format description
            // Overlay LZ compression is basically just LZ-0x10 compression.
            // however the order if reading is reversed: the compression starts at the end of the file.
            // Assuming we start reading at the end towards the beginning, the format is:
            /*
             * u32 extraSize; // decompressed data size = file length (including header) + this value
             * u8 headerSize;
             * u24 compressedLength; // can be less than file size (w/o header). If so, the rest of the file is uncompressed.
             *                       // may also be the file size
             * u8[headerSize-8] padding; // 0xFF-s
             * 
             * 0x10-like-compressed data follows (without the usual 4-byte header).
             * The only difference is that 2 should be added to the DISP value in compressed blocks
             * to get the proper value.
             * the u32 and u24 are read most significant byte first.
             * if extraSize is 0, there is no headerSize, decompressedLength or padding.
             * the data starts immediately, and is uncompressed.
             * 
             * arm9.bin has 3 extra u32 values at the 'start' (ie: end of the file),
             * which may be ignored. (and are ignored here) These 12 bytes also should not
             * be included in the computation of the output size.
             */
            #endregion

            long streamStart = instream.Position;

            #region First read the last 4 bytes of the stream (the 'extraSize')

            // first go to the end of the stream, since we're reading from back to front
            // read the last 4 bytes, the 'extraSize'
            instream.Position += inLength - 4;

            byte[] buffer = new byte[4];
            try
            {
                instream.Read(buffer, 0, 4);
            }
            catch (System.IO.EndOfStreamException)
            {
                // since we're immediately checking the end of the stream, 
                // this is the only location where we have to check for an EOS to occur.
                throw new Exception();
            }
            uint extraSize = ToNDSu32(buffer, 0);

            #endregion

            // if the extra size is 0, there is no compressed part, and the header ends there.
            if (extraSize == 0)
            {
                #region just copy the input to the output

                // first go back to the start of the file. the current location is after the 'extraSize',
                // and thus at the end of the file.
                instream.Position -= inLength;
                // no buffering -> slow
                buffer = new byte[inLength - 4];
                instream.Read(buffer, 0, (int)(inLength - 4));
                outstream.Write(buffer, 0, (int)(inLength - 4));

                // make sure the input is positioned at the end of the file
                instream.Position += 4;

                return inLength - 4;

                #endregion
            }
            else
            {
                // get the size of the compression header first.
                instream.Position -= 5;
                int headerSize = instream.ReadByte();

                // then the compressed data size.
                instream.Position -= 4;
                instream.Read(buffer, 0, 3);
                int compressedSize = buffer[0] | (buffer[1] << 8) | (buffer[2] << 16);

                // reset stream position
                instream.Position = streamStart;

                #region copy the non-compressed data

                long uncompressedSize = inLength - compressedSize;
                // copy the non-compressed data first.
                if (uncompressedSize > 0)
                {
                    buffer = new byte[uncompressedSize];
                    instream.Read(buffer, 0, buffer.Length);
                    outstream.Write(buffer, 0, buffer.Length);
                }

                #endregion

                // adjust for header bytes
                compressedSize -= headerSize;

                // buffer the compressed data, such that we don't need to keep
                // moving the input stream position back and forth
                buffer = new byte[compressedSize];
                instream.Read(buffer, 0, compressedSize);

                // make sure the input is positioned at the end of the file; the stream is currently
                // at the compression header.
                instream.Position += headerSize;

                // we're filling the output from end to start, so we can't directly write the data.
                // buffer it instead (also use this data as buffer instead of a ring-buffer for
                // decompression)
                byte[] outbuffer = new byte[compressedSize + headerSize + extraSize];

                int currentOutSize = 0;
                int decompressedLength = outbuffer.Length;
                int readBytes = 0;
                byte flags = 0, mask = 1;
                while (currentOutSize < decompressedLength)
                {
                    // (throws when requested new flags byte is not available)
                    #region Update the mask. If all flag bits have been read, get a new set.
                    // the current mask is the mask used in the previous run. So if it masks the
                    // last flag bit, get a new flags byte.
                    if (mask == 1)
                    {
                        if (readBytes >= compressedSize)
                            throw new Exception();
                        flags = buffer[buffer.Length - 1 - readBytes]; readBytes++;
                        mask = 0x80;
                    }
                    else
                    {
                        mask >>= 1;
                    }
                    #endregion

                    // bit = 1 <=> compressed.
                    if ((flags & mask) > 0)
                    {
                        // (throws when < 2 bytes are available)
                        #region Get length and displacement('disp') values from next 2 bytes
                        // there are < 2 bytes available when the end is at most 1 byte away
                        if (readBytes + 1 >= compressedSize)
                        {
                            throw new Exception();
                        }
                        int byte1 = buffer[compressedSize - 1 - readBytes]; readBytes++;
                        int byte2 = buffer[compressedSize - 1 - readBytes]; readBytes++;

                        // the number of bytes to copy
                        int length = byte1 >> 4;
                        length += 3;

                        // from where the bytes should be copied (relatively)
                        int disp = ((byte1 & 0x0F) << 8) | byte2;
                        disp += 3;

                        if (disp > currentOutSize)
                        {
                            if (currentOutSize < 2)
                                throw new Exception("Cannot go back more than already written; "
                                    + "attempt to go back 0x" + disp.ToString("X") + " when only 0x"
                                    + currentOutSize.ToString("X") + " bytes have been written.");
                            // HACK. this seems to produce valid files, but isn't the most elegant solution.
                            // although this _could_ be the actual way to use a disp of 2 in this format,
                            // as otherwise the minimum would be 3 (and 0 is undefined, and 1 is less useful).
                            disp = 2;
                        }
                        #endregion

                        int bufIdx = currentOutSize - disp;
                        for (int i = 0; i < length; i++)
                        {
                            byte next = outbuffer[outbuffer.Length - 1 - bufIdx];
                            bufIdx++;
                            outbuffer[outbuffer.Length - 1 - currentOutSize] = next;
                            currentOutSize++;
                        }
                    }
                    else
                    {
                        if (readBytes >= compressedSize)
                            throw new Exception();
                        byte next = buffer[buffer.Length - 1 - readBytes]; readBytes++;

                        outbuffer[outbuffer.Length - 1 - currentOutSize] = next;
                        currentOutSize++;
                    }
                }

                // write the decompressed data
                outstream.Write(outbuffer, 0, outbuffer.Length);

                return decompressedLength + (inLength - headerSize - compressedSize);
            }
        }

        public static uint ToNDSu32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                        | (buffer[offset + 1] << 8)
                        | (buffer[offset + 2] << 16)
                        | (buffer[offset + 3] << 24));
        }
    }
}
