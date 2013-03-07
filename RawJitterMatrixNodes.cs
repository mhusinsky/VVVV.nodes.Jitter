#region licence/info

//////project name
// Jitter Matrix Encoder/Decoder

//////description
// Decodes and Encodes matrix data for Max/MSP/Jitter
// 	Can receive data sent via jit.net.send and decode it
//	Can encode data to be recieved in Max with jit.net.recv
// For details how a Jitter matrix is constructed see
//	http://cycling74.com/sdk/MaxSDK-6.0.4/html/chapter_jit_networking.html
//

////// version history
// 130301: v0.1
//	. first alpha release
//	. basic functionality for encoding and decoding of 1 or 2 dimensional matrices of type 0 (char) implemented
//	. encodes from and decodes to a color spread
//	. heavy testing needed

////// TODO
// Decoder:
//	. reply with latency chunk (see Max API docs)
//  . ...
// Encoder:
//	. Encode time right
//	. handle more than 2 dimensions
// 	. ...

//////licence
//GNU Lesser General Public License (LGPL)
//english: http://www.gnu.org/licenses/lgpl.html
//german: http://www.gnu.de/lgpl-ger.html

//////initial author
// motzi (Matthias Husinsky -> mhusinsky[at]fhstp.ac.at)
// Institute for Creative\Media/Technologies
// University of Applied Sciences St.Pölten, Austria
// -> http://icmt.fhstp.ac.at

#endregion licence/info

#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Text;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using VVVV.Utils.Streams;

//using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(
	Name = "Jitter Matrix Decoder",
	Category = "Raw", Help = "Decodes matrices sent from Max via jit.net.send",
	Tags = "Max, Jitter, Network",
	Author = "motzi",
	AutoEvaluate = false
	)]
	#endregion PluginInfo
	public class RawJitterMatrixDecoderNode : IPluginEvaluate
	{
		#region fields & pins
		[Input("Input", IsSingle=true)]
		IDiffSpread<Stream> FStreamIn;
		
		[Output("ID", Visibility = PinVisibility.OnlyInspector)]
		ISpread<string> FId;
		
		[Output("Chunk Size", Visibility = PinVisibility.OnlyInspector)]
		ISpread<int> FChunkSize;
		
		[Output("Plane Count")]
		ISpread<int> FPlaneCount;
		
		[Output("Type", Visibility = PinVisibility.OnlyInspector)]
		ISpread<int> FType;
		
		[Output("Dim Count")]
		ISpread<int> FDimCount;
		
		[Output("Dim")]
		ISpread<int> FDim;
		
		[Output("Dimstride", Visibility = PinVisibility.OnlyInspector)]
		ISpread<int> FDimstride;
		
		[Output("Datasize", Visibility = PinVisibility.OnlyInspector)]
		ISpread<int> FDataSize;
		
		[Output("Time", Visibility = PinVisibility.OnlyInspector)]
		ISpread<double> FTime;
		
		//[Output("Debug")]
		//ISpread<int> FDebug;
		
		[Output("Data Value")]
		ISpread<int> FData;
		
		[Output("Data Color")]
		ISpread<RGBAColor> FDataCol;
		
		ByteOrder FBo = ByteOrder.BigEndian;
		ASCIIEncoding asciiEncoding = new ASCIIEncoding();
		#endregion fields & pins
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			if(!FStreamIn.IsChanged)
			return;
			
			FId.SliceCount = spreadMax;
			FChunkSize.SliceCount = spreadMax;
			//FDim.SliceCount = spreadMax;
			//FDimstride.SliceCount = spreadMax;
			
			for (int i = 0; i < spreadMax; i++)
			{
				//get the input stream
				var inputStream = FStreamIn[i];
				byte[] chunkId1 = new byte[4];
				int chunkSize1 = 0;
				
				
				byte[] chunkId2 = new byte[4];
				int chunkSize2 = 0;
				int planeCount = 0;
				int type = 0;
				int dimcount = 0;
				int[] dim = new int[32];
				int[] dimstride = new int[32];
				int datasize = 0;
				double time = 0;
				
				byte[] data;
				try{
					using (var r = new BinaryReader(FStreamIn[i]))
					{
						// First Chunk: int32 id (4 bytes containing chars), int32 size
						
						chunkId1 = r.ReadBytes(4);
						chunkSize1 = r.ReadInt32();
						
						chunkId2 = r.ReadBytes(4);
						chunkSize2 =  r.ReadInt32(FBo);
						planeCount = r.ReadInt32(FBo);
						type = r.ReadInt32(FBo);
						dimcount = r.ReadInt32(FBo);
						r.Read(dim, 0, 32, FBo);
						r.Read(dimstride, 0, 32, FBo);
						datasize = r.ReadInt32(FBo);
						time = r.ReadDouble(FBo);
						
						
						FId[i] = asciiEncoding.GetString(chunkId2);
						FChunkSize[i] = chunkSize2;
						FPlaneCount[i] = planeCount;
						FType[i] = type;
						FDimCount[i] = dimcount;
						
						FDim.SliceCount = dimcount;
						FDimstride.SliceCount = dimcount;
						
						for(int j=0; j<dimcount; j++)
						{
							FDim[j] = dim[j];
						}
						for(int j=0; j<dimcount; j++)
						{
							FDimstride[j] = dimstride[j];
						}
						
						FDataSize[i] = datasize;
						FTime[i] = time;
						
						// process data for output
						data = new byte[datasize];
						
						data = r.ReadBytes(datasize);
						
						int pixelCount = dim[0] * dim[1] * planeCount;
						int dataCount = (int)(datasize / planeCount);
						FData.SliceCount = pixelCount;
						FDataCol.SliceCount = dim[0] * dim[1];	// assumes 4 planes
						
						// how many bytes are used to fill up the block?
						int fillSize = (dimstride[0] - dim[0] % dimstride[0]) % dimstride[0];
						int xBlockSize = dim[0] + fillSize;
						
						// index of elements (e.g. one pixel having 4 planes)
						int index = 0;
						for(int j=0; j<dataCount; j++)
						{
							// only consider data that is not a "filler"
							if(j % xBlockSize < dim[0])
							{
								FData[index*planeCount] = data[j*planeCount];
								FData[index*planeCount+1] = data[j*planeCount+1];
								FData[index*planeCount+2] = data[j*planeCount+2];
								FData[index*planeCount+3] = data[j*planeCount+3];
								
								// assuming there are 4 planes (ARGB)
								RGBAColor col = new RGBAColor();
								col.A = data[j*planeCount] / 255.0;
								col.R = data[j*planeCount+1] / 255.0;
								col.G = data[j*planeCount+2] / 255.0;
								col.B = data[j*planeCount+3] / 255.0;
								FDataCol[index] = col;
								
								index++;
							}
						}
					}
				}
				catch (Exception e)
				{}
				
			}
		}
		
	}
	
	#region PluginInfo
	[PluginInfo(
	Name = "Jitter Matrix Encoder",
	Category = "Raw",
	Help = "Encodes matrices to be recieved in Max via jit.net.recieve",
	Tags = "Max, Jitter, Network",
	Author = "motzi",
	AutoEvaluate = false
	)]
	#endregion PluginInfo
	public class RawJitterMatrixEncoderNode : IPluginEvaluate, IPartImportsSatisfiedNotification
	{
		#region fields & pins
		[Input("Input", DefaultColor = new double[] { 1.0, 1.0, 1.0, 1.0 })]
		ISpread<RGBAColor> FColor;
		
		[Input("Dimension X", MinValue = 1, DefaultValue = 1)]
		ISpread<int> FDimX;
		
		[Input("Dimension Y", MinValue = 1, DefaultValue = 1)]
		ISpread<int> FDimY;
		
		[Input("Plane Count", MinValue = 1, DefaultValue=4)]
		ISpread<int> FPlaneCount;
		
		//[Output("Debug")]
		//ISpread<int> FDebug;
		
		[Output("Output")]
		ISpread<Stream> FStreamOut;
		
		#endregion fields & pins
		
		//called when all inputs and outputs defined above are assigned from the host
		public void OnImportsSatisfied()
		{
			//start with an empty stream output
			FStreamOut.SliceCount = 0;
		}
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			spreadMax = Math.Max(FDimX.SliceCount, FDimY.SliceCount);
			spreadMax = Math.Max(spreadMax, FPlaneCount.SliceCount);
			
			//ResizeAndDispose will adjust the spread length and thereby call
			//the given constructor function for new slices and Dispose on old
			//slices.
			FStreamOut.ResizeAndDispose(spreadMax, () => new MemoryStream());
			
			for (int i = 0; i < spreadMax; i++)
			{
				//get the output stream (this works because of ResizeAndDispose above)
				var outputStream = FStreamOut[i];
				outputStream.Position = 0;
				
				// actually an int32 according to Max6 API docs
				byte[] chunkId1 = new byte[4]{ 0x4A, 0x4D, 0x54, 0x58};	// JMTX
				int chunkSize1 = 288;
				
				// from here on everything is meant to be encoded big endian (see stream writing later)
				byte[] chunkId2 = new byte[4] { 0x4A, 0x4D, 0x54, 0x58};
				int chunkSize2 = 288;
				int planeCount = FPlaneCount[i];
				int type = 0;		// 0 for char, 1 for long, 2 for float32, 3 for float64
				int dimcount = 2;
				int[] dim = new int[32]
				{
					FDimX[i],
					FDimY[i],
					1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1
				};
				
				// details for calculation: http://cycling74.com/forums/topic.php?id=6677
				// padding for dimstride[2] apparently not nescessary for sending, warumauchimmer
				// int padding = 0 - (FPlaneCount[i]*FDimX[i] % 16);
				
				int[] dimstride = new int[32]
				{
					FPlaneCount[i],	// should actually be sizeof(type)*planecount
					FPlaneCount[i]*FDimX[i],
					0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
				};
				int datasize = FDimX[i]*FDimY[i]*planeCount;
				double time = 10765666.2262;
				
				outputStream.SetLength(296 + datasize);
				
				outputStream.Write(chunkId1, 0, 4);
				outputStream.Write(BitConverter.GetBytes(chunkSize1), 0, 4);
				
				outputStream.Write(ConvertEndian(chunkId2),0,4);
				outputStream.Write(ConvertEndian(chunkSize2),0,4);
				outputStream.Write(ConvertEndian(planeCount),0,4);
				outputStream.Write(ConvertEndian(type),0,4);
				outputStream.Write(ConvertEndian(dimcount),0,4);
				
				for (int j=0; j<dim.Length; j++)
				outputStream.Write(ConvertEndian(dim[j]),0,4);
				for (int j=0; j<dimstride.Length; j++)
				outputStream.Write(ConvertEndian(dimstride[j]),0,4);
				outputStream.Write(ConvertEndian(datasize),0,4);
				outputStream.Write(ConvertEndian(time),0,8);
				
				for (int j=0; j<FDimX[i]*FDimY[i]; j++)
				{
					byte[] ARGB = new byte[4];
					ARGB[0] = (byte)Math.Round(FColor[j].A * 255);
					ARGB[1] = (byte)Math.Round(FColor[j].R * 255);
					ARGB[2] = (byte)Math.Round(FColor[j].G * 255);
					ARGB[3] = (byte)Math.Round(FColor[j].B * 255);
					outputStream.Write(ARGB,0,4);
				}
				
			}
			//this will force the changed flag of the output pin to be set
			FStreamOut.Flush(true);
		}
		
		public byte[] ConvertEndian(byte[] buffer)
		{
			Array.Reverse(buffer);
			return buffer;
		}
		
		public byte[] ConvertEndian(int num)
		{
			byte[] buffer = BitConverter.GetBytes(num);
			Array.Reverse(buffer);
			return buffer;
		}
		
		public byte[] ConvertEndian(long num)
		{
			byte[] buffer = BitConverter.GetBytes(num);
			Array.Reverse(buffer);
			return buffer;
		}
		
		public byte[] ConvertEndian(double num)
		{
			byte[] buffer = BitConverter.GetBytes(num);
			Array.Reverse(buffer);
			return buffer;
		}
	}
}
