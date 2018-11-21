using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using System.Xml;

class Program
{
    static byte[] AdjustTileId(uint tileId, uint rot, bool flipX)
    {
        uint flipState = 0;
        if(flipX)
        {
            switch(rot)
            {
                case 0:
                    flipState = 0x80000000;       // horizontal
                    break;
                case 1:
                    flipState = 0xE0000000;       // horizontal + vertical + diagonal
                    break;
                case 2:
                    flipState = 0x40000000;       // vertical
                    break;
                case 3:
                    flipState = 0x20000000;       // diagonal
                    break;
            }
        }
        else
        {
            switch(rot)
            {
                case 0:                            // no change
                    break;
                case 1:
                    flipState = 0xA0000000;       // horizontal + diagonal
                    break;
                case 2:
                    flipState = 0xC0000000;       // horizontal + vertical
                    break;
                case 3:
                    flipState = 0x60000000;       // vertical + diagonal
                    break;
            }
        }
        tileId++;
        tileId |= flipState;
        return BitConverter.GetBytes(tileId);
    }

    static byte[] Compress(byte[] data)
    {
        using (var compressedStream = new MemoryStream())
        using (var zipStream = new GZipStream(compressedStream, CompressionMode.Compress))
        {
            zipStream.Write(data, 0, data.Length);
            zipStream.Close();
            return compressedStream.ToArray();
        }
    }

    static byte[] ReverseArray(byte[] array)
    {
        for (int i = 0; i < array.Length / 2; i++)
        {
           byte tmp = array[i];
           array[i] = array[array.Length - i - 1];
           array[array.Length - i - 1] = tmp;
        }
        return array;
    }


    public struct Layer
    {
        public uint Id;
        public string Name;
        public byte[] TileData;
        public byte[] CompressedTileData;
    }

    static int Main(string[] args)
    {
        if(args.Length == 0)
        {
            Console.WriteLine("===========");
            Console.WriteLine("pyxel2tiled");
            Console.WriteLine("===========");
            Console.WriteLine("Converts xml tilemaps exported by Pyxel Edit");
            Console.WriteLine("to the Tiled format (tmx).");
            Console.WriteLine();
            Console.WriteLine("Usage: " + Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location) + " file.xml");
            return 1;
        }
        
        string tmxName = Path.ChangeExtension(args[0], ".tmx");
        uint mapHeight = 0;
        uint mapWidth = 0;
        uint tileHeight = 0;
        uint tileWidth = 0;
        uint mapTileCount = 0;
        uint currentTile = 0;
        uint tileId = 0;
        uint rotation = 0;
        bool flipX = false;
        byte[] fixedTileId = new byte[4];
        
        Layer currentLayer = new Layer();
        List<Layer> layerList = new List<Layer>();
        
        //read xml
        using (XmlReader pyxelXML = XmlReader.Create(args[0]))
        {
            while (pyxelXML.Read())
            {
                if (pyxelXML.IsStartElement())  
                {
                    if(pyxelXML.Name == "tilemap")
                    {
                        while (pyxelXML.MoveToNextAttribute())
                        {
                            if (string.IsNullOrEmpty(pyxelXML.Value))
                            {
                                Console.WriteLine("Something wrong with the {0} element", pyxelXML.Name);
                                return 1;
                            }
                        
                            switch(pyxelXML.Name)
                            {
                                case "tileswide":
                                    mapWidth = UInt32.Parse(pyxelXML.Value);
                                    break;
                                case "tileshigh":
                                    mapHeight = UInt32.Parse(pyxelXML.Value);
                                    break;
                                case "tilewidth":
                                    tileWidth = UInt32.Parse(pyxelXML.Value);
                                    break;
                                case "tileheight":
                                    tileHeight = UInt32.Parse(pyxelXML.Value);
                                    break;
                            }
                        }
                        mapTileCount = mapWidth * mapHeight;
                        currentLayer.TileData = new byte [mapTileCount * 4];                       // set tiledata size
                    }
                    
                    if(pyxelXML.Name == "layer")
                    {
                        while (pyxelXML.MoveToNextAttribute())
                        {
                            if (string.IsNullOrEmpty(pyxelXML.Value))
                            {
                                Console.WriteLine("Something wrong with the {0} element", pyxelXML.Name);
                                return 1;
                            }

                            switch(pyxelXML.Name)
                            {
                                case "number":
                                    currentLayer.Id = UInt32.Parse(pyxelXML.Value) + 1;
                                    break;
                                
                                case "name":
                                    currentLayer.Name = pyxelXML.Value;
                                    break;
                            }

                        }
                    }
                    
                    if(pyxelXML.Name == "tile")
                    {
                        while (pyxelXML.MoveToNextAttribute())
                        {
                            if (string.IsNullOrEmpty(pyxelXML.Value))
                            {
                                Console.WriteLine("Something wrong with the {0} element", pyxelXML.Name);
                                return 1;
                            }
                            
                            switch(pyxelXML.Name)
                            {
                                case "tile":
                                    tileId = UInt32.Parse(pyxelXML.Value);
                                    break;
                                case "rot":
                                    rotation = UInt32.Parse(pyxelXML.Value);
                                    break;
                                case "flipX":
                                    flipX = Boolean.Parse(pyxelXML.Value);
                                    break;
                            }
                        }
                        
                        fixedTileId = AdjustTileId(tileId, rotation, flipX);                  // firstgid is 1
                        Buffer.BlockCopy(fixedTileId, 0, currentLayer.TileData, (int)currentTile * 4, 4);  // add tileid to tiledata
                        
                        if(currentTile == (mapTileCount -1))
                        {
                            currentTile = 0;                                        // reinitialize for another possible layer
                            currentLayer.CompressedTileData = Compress(currentLayer.TileData);
                            layerList.Add(currentLayer);                            // add current layer to List
                        }
                        
                        else
                            currentTile++;
                    }
                }
            }
        }

        // get png dimensions
        string pngName = Path.ChangeExtension(args[0], ".png");
        byte[] magicBytes = new byte[]{ 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bool isPng = true;
        uint pngWidth = 0;
        uint pngHeight = 0;
        uint tileCount = 0;
        
        if(File.Exists(pngName))
        {
            using (BinaryReader binaryReader = new BinaryReader(File.OpenRead(pngName)))
            {
                byte[] test = binaryReader.ReadBytes(8);
                
                for(int i = 0; i < 8; i++)
                {
                    if(test[i] != magicBytes[i])
                    {
                        Console.WriteLine("{0} exists but it's not a PNG image.", pngName);
                        Console.WriteLine("Open and save the tmx in Tiled to add the missing tilemap info.", pngName);
                        isPng = false;
                        break;
                    }
                }
                
                if(isPng)
                {
                    binaryReader.ReadBytes(8);          // skip 8 bytes ahead
                    pngWidth = BitConverter.ToUInt32(ReverseArray(binaryReader.ReadBytes(4)), 0);
                    pngHeight = BitConverter.ToUInt32(ReverseArray(binaryReader.ReadBytes(4)), 0);
                    Console.WriteLine("png width: {0}, png height: {1}", pngWidth, pngHeight);
                }
            }
        }
        else
        {
            Console.WriteLine("{0} not found.", pngName);
            Console.WriteLine("Open and save the tmx in Tiled to add the missing tilemap info.", pngName);
            isPng = false;
        }

        // write tmx
        XmlWriterSettings settings = new XmlWriterSettings();
        settings.Indent = true;
        using (XmlWriter tiledTMX = XmlWriter.Create(tmxName, settings))
        {
            tiledTMX.WriteStartDocument();
            tiledTMX.WriteStartElement("map");
            tiledTMX.WriteAttributeString("version", "1.1");
            tiledTMX.WriteAttributeString("width", mapWidth.ToString());
            tiledTMX.WriteAttributeString("height", mapHeight.ToString());
            tiledTMX.WriteAttributeString("tilewidth", tileWidth.ToString());
            tiledTMX.WriteAttributeString("tileheight", tileHeight.ToString());
            tiledTMX.WriteAttributeString("orientation", "orthogonal");
            
            tiledTMX.WriteStartElement("tileset");
            tiledTMX.WriteAttributeString("firstgid","1");
            tiledTMX.WriteAttributeString("name","Converted by pyxel2tiled");
            tiledTMX.WriteAttributeString("tilewidth",tileWidth.ToString());
            tiledTMX.WriteAttributeString("tileheight",tileHeight.ToString());
            if(isPng)
            {
                tileCount = (pngWidth / tileWidth) * (pngHeight / tileHeight);
                tiledTMX.WriteAttributeString("tilecount",tileCount.ToString());///////////////////////////////////////////////////////
            }
                
            tiledTMX.WriteStartElement("image");
            tiledTMX.WriteAttributeString("source",Path.GetFileNameWithoutExtension(args[0]) + ".png");
            if(isPng)
            {
                tiledTMX.WriteAttributeString("width", pngWidth.ToString());
                tiledTMX.WriteAttributeString("height", pngHeight.ToString());
            }
            tiledTMX.WriteEndElement();                     // close "image"
            tiledTMX.WriteEndElement();                     // close "tileset"
            
            for(int i = 0; i < layerList.Count; i++)
            {
                tiledTMX.WriteStartElement("layer");
                tiledTMX.WriteAttributeString("width", mapWidth.ToString());
                tiledTMX.WriteAttributeString("height", mapHeight.ToString());
                tiledTMX.WriteAttributeString("id", layerList[i].Id.ToString());
                tiledTMX.WriteAttributeString("name", layerList[i].Name);
                tiledTMX.WriteStartElement("data");
                tiledTMX.WriteAttributeString("encoding", "base64");
                tiledTMX.WriteAttributeString("compression", "gzip");
                tiledTMX.WriteString(System.Convert.ToBase64String(layerList[i].CompressedTileData));

                tiledTMX.WriteEndElement();                 // close "data"
                tiledTMX.WriteEndElement();                 // close "layer"
            }
        }
        Console.WriteLine("{0} successfully converted to {1}!", args[0], tmxName);
        return 0;
    }
}
