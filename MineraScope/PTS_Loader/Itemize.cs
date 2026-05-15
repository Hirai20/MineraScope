// PTS2HDF5, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// PTS2HDF5.Itemize
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Xml;


public enum ItemizeType
{
    Unknown,
    Char,
    Short,
    Long,
    Float,
    Double,
    CharArray,
    ShortArray,
    LongArray,
    FloatArray,
    DoubleArray,
    Bool,
    String,
    MultiString,
    Selectable,
    ShortInfo,
    LongInfo,
    FloatInfo,
    DoubleInfo,
    StringInfo,
    SharedMemory
}


public class Itemize
{
    private string itemName = "";

    private ItemizeType itemType;

    private byte[] itemData;

    private Itemize parent;

    private OrderedDictionary children;

    private Encoding encode;

    private bool readOnly = true;

    private readonly string[] _data_type = new string[20]
    {
        "DT_UNKNOWN", "DT_CHAR", "DT_SHORT", "DT_LONG", "DT_FLOAT", "DT_DOUBLE", "DT_CHAR_ARRAY", "DT_SHORT_ARRAY", "DT_LONG_ARRAY", "DT_FLOAT_ARRAY",
        "DT_DOUBLE_ARRAY", "DT_BOOL", "DT_STRING", "DT_MULTSTRING", "DT_SELECTABLE", "DT_SHORT_INFO", "DT_LONG_INFO", "DT_FLOAT_INFO", "DT_DOUBLE_INFO", "DT_STR_INFO"
    };

    public string Name => itemName;

    public ItemizeType ItemType => itemType;

    public byte[] ItemData => itemData;

    private Itemize Parent => parent;

    public int ChildNum => children.Count;

    public void SetName(string name)
    {
        itemName = name;
    }

    public void SetItemizeType(ItemizeType type)
    {
        itemType = type;
    }

    private void setEnc()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        encode = Encoding.GetEncoding("Shift_JIS");
    }

    public Itemize()
    {
        
        setEnc();
        children = new OrderedDictionary();
    }

    public Itemize(BinaryReader reader)
    {
        setEnc();
        children = new OrderedDictionary();
        Read(reader);
        while (reader.ReadSByte() > 0)
        {
            Itemize itemize = new Itemize(reader, this);
            try
            {
                children.Add(itemize.itemName, itemize);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    public Itemize(byte[] bitem, ref int pos)
    {
        setEnc();
        children = new OrderedDictionary();
        Read(bitem, ref pos);
        while (bitem.Length > pos && (sbyte)bitem[pos++] > 0)
        {
            Itemize itemize = new Itemize(bitem, ref pos, this);
            children.Add(itemize.itemName, itemize);
        }
    }

    public Itemize(byte[] pt, ref int pos, Itemize parent)
        : this(pt, ref pos)
    {
        setEnc();
        this.parent = parent;
    }

    public Itemize(BinaryReader reader, Itemize parent)
        : this(reader)
    {
        setEnc();
        this.parent = parent;
    }

    public static bool SetItem(ref Itemize item, byte[] bitem)
    {
        bool result = true;
        try
        {
            int pos = 0;
            item = new Itemize(bitem, ref pos);
        }
        catch
        {
            result = false;
        }
        return result;
    }

    public void ConvertMemBlock(ref byte[] mblock)
    {
        List<byte> mblock2 = new List<byte>();
        GetMemBlock(ref mblock2);
        mblock = mblock2.ToArray();
    }

    private void GetMemBlock(ref List<byte> mblock)
    {
        if (itemName.Length > 0)
        {
            mblock.AddRange(BitConverter.GetBytes(itemName.Length + 1));
            mblock.AddRange(encode.GetBytes(itemName.ToCharArray()));
            mblock.Add(0);
        }
        else
        {
            mblock.AddRange(BitConverter.GetBytes(0));
            mblock.Add(0);
        }
        mblock.AddRange(BitConverter.GetBytes((short)itemType));
        mblock.AddRange(BitConverter.GetBytes(Convert.ToInt16(readOnly)));
        if (itemData != null)
        {
            mblock.AddRange(BitConverter.GetBytes(itemData.Length));
            if (itemData.Length != 0)
            {
                mblock.AddRange(itemData);
            }
        }
        else
        {
            mblock.AddRange(BitConverter.GetBytes(0));
        }
        if (children.Count > 0)
        {
            for (int i = 0; i < children.Count; i++)
            {
                Itemize obj = (Itemize)children[i];
                mblock.Add(1);
                obj.GetMemBlock(ref mblock);
            }
        }
        mblock.Add(byte.MaxValue);
    }

    public static bool LoadItem(ref Itemize item, string filename)
    {
        bool result = true;
        try
        {
            using FileStream fileStream = new FileStream(filename, FileMode.Open);
            using BinaryReader binaryReader = new BinaryReader(fileStream);
            item = new Itemize(binaryReader);
            binaryReader.Close();
            fileStream.Close();
        }
        catch
        {
            result = false;
        }
        return result;
    }

    public static bool SaveItem(Itemize item, string filename)
    {
        bool result = true;
        try
        {
            Path.GetDirectoryName(filename);
            using FileStream fileStream = new FileStream(filename, FileMode.Create);
            using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
            item.Write(binaryWriter);
            binaryWriter.Flush();
            fileStream.Flush();
            binaryWriter.Close();
            fileStream.Close();
        }
        catch
        {
            result = false;
        }
        return result;
    }

    protected void Read(byte[] bitem, ref int pos)
    {
        int num = BitConverter.ToInt32(bitem, pos);
        pos += 4;
        if (num > 0)
        {
            itemName = encode.GetString(bitem, pos, num).TrimEnd(default(char));
            pos += num;
        }
        itemType = (ItemizeType)BitConverter.ToUInt16(bitem, pos);
        pos += 2;
        readOnly = Convert.ToBoolean(BitConverter.ToUInt16(bitem, pos));
        pos += 2;
        int num2 = BitConverter.ToInt32(bitem, pos);
        pos += 4;
        if (num2 > 0)
        {
            byte[] destinationArray = new byte[num2];
            Array.Copy(bitem, pos, destinationArray, 0, num2);
            pos += num2;
            itemData = destinationArray;
        }
    }

    protected void Read(BinaryReader reader)
    {
        int num = reader.ReadInt32();
        if (num > 0)
        {
            itemName = encode.GetString(reader.ReadBytes(num), 0, num).TrimEnd(default(char));
        }
        itemType = (ItemizeType)reader.ReadUInt16();
        readOnly = Convert.ToBoolean(reader.ReadUInt16());
        int num2 = reader.ReadInt32();
        if (num2 > 0)
        {
            itemData = reader.ReadBytes(num2);
        }
    }

    protected void Write(BinaryWriter writer)
    {
        if (itemName.Length > 0)
        {
            writer.Write(itemName.Length + 1);
            writer.Write(encode.GetBytes(itemName.ToCharArray()));
            writer.Write('\0');
        }
        else
        {
            writer.Write(1);
            writer.Write('\0');
        }
        writer.Write((short)itemType);
        writer.Write(Convert.ToInt16(readOnly));
        if (itemData != null)
        {
            writer.Write(itemData.Length);
            if (itemData.Length != 0)
            {
                writer.Write(itemData);
            }
        }
        else
        {
            writer.Write(0);
        }
        if (children.Count > 0)
        {
            for (int i = 0; i < children.Count; i++)
            {
                Itemize obj = (Itemize)children[i];
                writer.Write('\u0001');
                obj.Write(writer);
            }
        }
        writer.Write(byte.MaxValue);
    }

    public void Write(XmlWriter xml)
    {
        xml.WriteStartElement("Itemize");
        xml.WriteAttributeString("name", itemName);
        xml.WriteAttributeString("type", itemType.ToString());
        string text = ToString();
        if (text != null)
        {
            xml.WriteAttributeString("data", text);
        }
        for (int i = 0; i < children.Count; i++)
        {
            ((Itemize)children[i]).Write(xml);
        }
        xml.WriteEndElement();
    }

    public Itemize GetItemExc(string name)
    {
        return GetItem(name) ?? throw new ArgumentException($"Itemizeデータ内に[{name}]という名前のアイテムが見つかりませんでした。");
    }

    public Itemize GetItem(string name)
    {
        int num = -1;
        string text = "";
        string empty = string.Empty;
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            empty = name.Substring(num);
            return ((Itemize)children[text]).GetItem(empty);
        }
        if (children.Contains(name))
        {
            return (Itemize)children[name];
        }
        return null;
    }

    public int AddItem(Itemize item)
    {
        children.Add(item.itemName, item);
        return children.Count;
    }

    public virtual void SetItem(Itemize item)
    {
        children[item.itemName] = item;
    }

    public void AppendItemTree(Itemize item)
    {
        for (int i = 0; i < item.children.Count; i++)
        {
            Itemize childItem = item.GetChildItem(i);
            AddItem(childItem);
        }
    }

    public void DeleteChild(int n)
    {
        try
        {
            children.RemoveAt(n);
        }
        catch
        {
        }
    }

    public void DeleteChild(string itemname)
    {
        try
        {
            int num = -1;
            string text = "";
            string empty = string.Empty;
            if ((num = itemname.IndexOf("\\")) != -1)
            {
                text = itemname.Substring(0, num);
                num++;
                empty = itemname.Substring(num);
                ((Itemize)children[text]).DeleteChild(empty);
            }
            else if (children.Contains(itemname))
            {
                children.Remove(itemname);
            }
        }
        catch
        {
        }
    }

    public void DeleteAllChild()
    {
        children.Clear();
    }

    public Itemize GetFirstItem(string baseName)
    {
        for (int i = 0; i < children.Count; i++)
        {
            Itemize itemize = (Itemize)children[i];
            if (string.Compare(baseName, 0, itemize.Name, 0, baseName.Length) == 0)
            {
                return itemize;
            }
        }
        return null;
    }

    public Itemize GetChildItem(string treeName)
    {
        int num = treeName.IndexOf('\\');
        if (num > 0)
        {
            string key = treeName.Substring(0, num);
            string treeName2 = treeName.Substring(num + 1);
            return ((Itemize)children[key]).GetChildItem(treeName2);
        }
        return (Itemize)children[treeName];
    }

    public override string ToString()
    {
        string result = string.Empty;
        try
        {
            switch (itemType)
            {
                case ItemizeType.String:
                    result = GetString();
                    break;
                case ItemizeType.Char:
                    result = GetChar().ToString();
                    break;
                case ItemizeType.Bool:
                    result = GetBoolean().ToString();
                    break;
                case ItemizeType.Short:
                    result = GetInt16().ToString();
                    break;
                case ItemizeType.Long:
                    result = GetInt32().ToString();
                    break;
                case ItemizeType.Float:
                    result = GetSingle().ToString();
                    break;
                case ItemizeType.Double:
                    result = GetDouble().ToString();
                    break;
                case ItemizeType.Selectable:
                    result = GetInt16().ToString();
                    break;
                case ItemizeType.MultiString:
                    {
                        string[] stringArray = GetStringArray();
                        if (stringArray.Length != 0)
                        {
                            StringBuilder stringBuilder5 = new StringBuilder();
                            stringBuilder5.Append(stringArray[0]);
                            for (int m = 1; m < stringArray.Length; m++)
                            {
                                stringBuilder5.Append("," + stringArray[m]);
                            }
                            result = stringBuilder5.ToString();
                        }
                        break;
                    }
                case ItemizeType.CharArray:
                    {
                        byte[] charArray = GetCharArray();
                        if (charArray.Length != 0)
                        {
                            StringBuilder stringBuilder2 = new StringBuilder();
                            stringBuilder2.Append(charArray[0]);
                            for (int j = 1; j < charArray.Length; j++)
                            {
                                stringBuilder2.Append("," + charArray[j]);
                            }
                            result = stringBuilder2.ToString();
                        }
                        break;
                    }
                case ItemizeType.ShortArray:
                    {
                        short[] int16Array = GetInt16Array();
                        if (int16Array.Length != 0)
                        {
                            StringBuilder stringBuilder6 = new StringBuilder();
                            stringBuilder6.Append(int16Array[0]);
                            for (int n = 1; n < int16Array.Length; n++)
                            {
                                stringBuilder6.Append("," + int16Array[n]);
                            }
                            result = stringBuilder6.ToString();
                        }
                        break;
                    }
                case ItemizeType.LongArray:
                    {
                        int[] int32Array = GetInt32Array();
                        if (int32Array.Length != 0)
                        {
                            StringBuilder stringBuilder4 = new StringBuilder();
                            stringBuilder4.Append(int32Array[0]);
                            for (int l = 1; l < int32Array.Length; l++)
                            {
                                stringBuilder4.Append("," + int32Array[l]);
                            }
                            result = stringBuilder4.ToString();
                        }
                        break;
                    }
                case ItemizeType.FloatArray:
                    {
                        float[] singleArray = GetSingleArray();
                        if (singleArray.Length != 0)
                        {
                            StringBuilder stringBuilder3 = new StringBuilder();
                            stringBuilder3.Append(singleArray[0]);
                            for (int k = 1; k < singleArray.Length; k++)
                            {
                                stringBuilder3.Append("," + singleArray[k]);
                            }
                            result = stringBuilder3.ToString();
                        }
                        break;
                    }
                case ItemizeType.DoubleArray:
                    {
                        double[] doubleArray = GetDoubleArray();
                        if (doubleArray.Length != 0)
                        {
                            StringBuilder stringBuilder = new StringBuilder();
                            stringBuilder.Append(doubleArray[0]);
                            for (int i = 1; i < doubleArray.Length; i++)
                            {
                                stringBuilder.Append("," + doubleArray[i]);
                            }
                            result = stringBuilder.ToString();
                        }
                        break;
                    }
                case ItemizeType.FloatInfo:
                    {
                        float Default4 = 0f;
                        float Min4 = 0f;
                        float Max4 = 0f;
                        float Step4 = 0f;
                        string Format4 = "";
                        string Unit4 = "";
                        GetFloatInfo(ref Default4, ref Min4, ref Max4, ref Step4, ref Format4, ref Unit4);
                        result = $"{Default4},{Min4},{Max4},{Step4},\"{Format4}\",\"{Unit4}\"";
                        break;
                    }
                case ItemizeType.LongInfo:
                    {
                        int Default3 = 0;
                        int Min3 = 0;
                        int Max3 = 0;
                        int Step3 = 0;
                        string Format3 = "";
                        string Unit3 = "";
                        GetLongInfo(ref Default3, ref Min3, ref Max3, ref Step3, ref Format3, ref Unit3);
                        result = $"{Default3},{Min3},{Max3},{Step3},\"{Format3}\",\"{Unit3}\"";
                        break;
                    }
                case ItemizeType.ShortInfo:
                    {
                        short Default2 = 0;
                        short Min2 = 0;
                        short Max2 = 0;
                        short Step2 = 0;
                        string Format2 = "";
                        string Unit2 = "";
                        GetShortInfo(ref Default2, ref Min2, ref Max2, ref Step2, ref Format2, ref Unit2);
                        result = $"{Default2},{Min2},{Max2},{Step2},\"{Format2}\",\"{Unit2}\"";
                        break;
                    }
                case ItemizeType.DoubleInfo:
                    {
                        double Default = 0.0;
                        double Min = 0.0;
                        double Max = 0.0;
                        double Step = 0.0;
                        string Format = "";
                        string Unit = "";
                        GetDoubleInfo(ref Default, ref Min, ref Max, ref Step, ref Format, ref Unit);
                        result = $"{Default},{Min},{Max},{Step},\"{Format}\",\"{Unit}\"";
                        break;
                    }
                case ItemizeType.Unknown:
                    break;
            }
        }
        catch
        {
        }
        return result;
    }

    public void SetItem(string name, ItemizeType type)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetItem(text2, type);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetItem(text2, type);
            AddItem(itemize);
        }
        else if (children.Contains(name))
        {
            itemType = type;
            itemData = null;
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.itemType = type;
            itemize2.itemName = name;
            AddItem(itemize2);
        }
    }

    public string GetString()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return encode.GetString(itemData, 0, itemData.Length).TrimEnd(default(char));
    }

    public void SetString(string value)
    {
        itemType = ItemizeType.String;
        value += "\0";
        itemData = Encoding.GetEncoding("Shift-JIS").GetBytes(value);
    }

    public void SetString(string name, string value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetString(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetString(text2, value);
            AddItem(itemize);
        }
        else if (children.Contains(name))
        {
            GetItemExc(name).SetString(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetString(value);
            AddItem(itemize2);
        }
    }

    public string GetString(string name, string defaultValue)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetString();
            }
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetString(string name, ref string value)
    {
        try
        {
            int num = -1;
            string text = "";
            string text2 = "";
            if ((num = name.IndexOf("\\")) != -1)
            {
                text = name.Substring(0, num);
                num++;
                text2 = name.Substring(num);
                if (children.Contains(text))
                {
                    return ((Itemize)children[text]).GetString(text2, ref value);
                }
                return false;
            }
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public char GetChar()
    {
        try
        {
            return Convert.ToChar(itemData[0]);
        }
        catch
        {
            throw new InvalidCastException();
        }
    }

    public void SetChar(char value)
    {
        itemType = ItemizeType.Char;
        itemData = BitConverter.GetBytes(value);
    }

    public void SetChar(string name, char value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetChar(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetChar(text2, value);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetChar(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetChar(value);
            AddItem(itemize2);
        }
    }

    public char GetChar(string name, char defaultValue)
    {
        try
        {
            return ((Itemize)children[name])?.GetChar() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetChar(string name, ref char value)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetChar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public short GetInt16()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return BitConverter.ToInt16(itemData, 0);
    }

    public void SetInt16(short value)
    {
        itemType = ItemizeType.Short;
        itemData = BitConverter.GetBytes(value);
    }

    public void SetInt16(string name, short value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetInt16(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetInt16(text2, value);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetInt16(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetInt16(value);
            AddItem(itemize2);
        }
    }

    public short GetInt16(string name, short defaultValue)
    {
        try
        {
            return ((Itemize)children[name])?.GetInt16() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetInt16(string name, ref short value)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetInt16();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public short GetInt16(string name)
    {
        return ((Itemize)children[name]).GetInt16();
    }

    public int GetInt32()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return BitConverter.ToInt32(itemData, 0);
    }

    public void SetInt32(int value)
    {
        itemType = ItemizeType.Long;
        itemData = BitConverter.GetBytes(value);
    }

    public void SetInt32(string name, int value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetInt32(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetInt32(text2, value);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetInt32(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetInt32(value);
            AddItem(itemize2);
        }
    }

    public int GetInt32(string name, int defaultValue)
    {
        try
        {
            return ((Itemize)children[name])?.GetInt32() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetInt32(string name, ref int value)
    {
        try
        {
            int num = -1;
            string text = "";
            string text2 = "";
            if ((num = name.IndexOf("\\")) != -1)
            {
                text = name.Substring(0, num);
                num++;
                text2 = name.Substring(num);
                if (children.Contains(text))
                {
                    return ((Itemize)children[text]).GetInt32(text2, ref value);
                }
                return false;
            }
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetInt32();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public float GetSingle()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return BitConverter.ToSingle(itemData, 0);
    }

    public void SetSingle(float value)
    {
        itemType = ItemizeType.Float;
        itemData = BitConverter.GetBytes(value);
    }

    public void SetSingle(string name, float value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetSingle(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetSingle(text2, value);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetSingle(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetSingle(value);
            AddItem(itemize2);
        }
    }

    public float GetSingle(string name, float defaultValue)
    {
        try
        {
            return ((Itemize)children[name])?.GetSingle() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetSingle(string name, ref float value)
    {
        try
        {
            int num = -1;
            string text = "";
            string text2 = "";
            if ((num = name.IndexOf("\\")) != -1)
            {
                text = name.Substring(0, num);
                num++;
                text2 = name.Substring(num);
                if (children.Contains(text))
                {
                    return ((Itemize)children[text]).GetSingle(text2, ref value);
                }
                return false;
            }
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetSingle();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public float GetSingle(string name)
    {
        return ((Itemize)children[name]).GetSingle();
    }

    public double GetDouble()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return BitConverter.ToDouble(itemData, 0);
    }

    public void SetDouble(double value)
    {
        itemType = ItemizeType.Double;
        itemData = BitConverter.GetBytes(value);
    }

    public void SetDouble(string name, double value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetDouble(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetDouble(text2, value);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetDouble(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetDouble(value);
            AddItem(itemize2);
        }
    }

    public double GetDouble(string name, double defaultValue)
    {
        try
        {
            return ((Itemize)children[name])?.GetDouble() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetDouble(string name, ref double value)
    {
        try
        {
            int num = -1;
            string text = "";
            string text2 = "";
            if ((num = name.IndexOf("\\")) != -1)
            {
                text = name.Substring(0, num);
                num++;
                text2 = name.Substring(num);
                if (children.Contains(text))
                {
                    return ((Itemize)children[text]).GetDouble(text2, ref value);
                }
                return false;
            }
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetDouble();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool GetBoolean()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return BitConverter.ToBoolean(itemData, 0);
    }

    public void SetBoolean(bool value)
    {
        itemType = ItemizeType.Bool;
        itemData = BitConverter.GetBytes(value);
    }

    public void SetBoolean(string name, bool value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetBoolean(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetBoolean(text2, value);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetBoolean(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetBoolean(value);
            AddItem(itemize2);
        }
    }

    public bool GetBoolean(string name, bool defaultValue)
    {
        try
        {
            return ((Itemize)children[name])?.GetBoolean() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetBoolean(string name, ref bool value)
    {
        try
        {
            int num = -1;
            string text = "";
            string text2 = "";
            if ((num = name.IndexOf("\\")) != -1)
            {
                text = name.Substring(0, num);
                num++;
                text2 = name.Substring(num);
                if (children.Contains(text))
                {
                    return ((Itemize)children[text]).GetBoolean(text2, ref value);
                }
                return false;
            }
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetBoolean();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string[] GetStringArray()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return encode.GetString(itemData, 0, itemData.Length).TrimEnd(default(char)).Split(default(char));
    }

    public void SetStringArray(string[] array)
    {
        itemType = ItemizeType.MultiString;
        string s = string.Join("\0", array);
        itemData = Encoding.GetEncoding("Shift-JIS").GetBytes(s);
    }

    public void SetStringArray(string name, string[] array)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetStringArray(text2, array);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetStringArray(text2, array);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetStringArray(array);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetStringArray(array);
            AddItem(itemize2);
        }
    }

    public string[] GetStringArray(string name, string[] defaultValue)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetStringArray();
            }
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetStringArray(string name, ref string[] value)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetStringArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public byte[] GetByteArray()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        int num = itemData.Length;
        byte[] array = new byte[num];
        for (int i = 0; i < num; i++)
        {
            array[i] = itemData[i];
        }
        return array;
    }

    public void SetByteArray(byte[] array)
    {
        itemType = ItemizeType.CharArray;
        int num = array.Length;
        itemData = new byte[num];
        for (int i = 0; i < num; i++)
        {
            //itemData.CopyTo(BitConverter.GetBytes(array[i]), i);
            itemData.CopyTo(BitConverter.GetBytes((short)array[i]), i);
        }
    }

    public void SetByteArray(string name, byte[] array)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetByteArray(text2, array);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetByteArray(text2, array);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetByteArray(array);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetByteArray(array);
            AddItem(itemize2);
        }
    }

    public byte[] GetByteArray(string name, byte[] defaultValue)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetByteArray();
            }
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool GetByteArray(string name, ref byte[] value)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            value = itemize.GetByteArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string[] GetBoolean(string name, string[] defaultValues)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetStringArray();
            }
            return defaultValues;
        }
        catch
        {
            return defaultValues;
        }
    }

    public bool GetBoolean(string name, ref string[] values)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            values = itemize.GetStringArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public byte[] GetCharArray()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        int num = itemData.Length;
        byte[] array = new byte[num];
        for (int i = 0; i < num; i++)
        {
            array[i] = itemData[i];
        }
        return array;
    }

    public byte[] GetBoolean(string name, byte[] defaultValues)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetCharArray();
            }
            return defaultValues;
        }
        catch
        {
            return defaultValues;
        }
    }

    public bool GetBoolean(string name, ref byte[] values)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            values = itemize.GetCharArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public short[] GetInt16Array()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        int num = itemData.Length / 2;
        short[] array = new short[num];
        for (int i = 0; i < num; i++)
        {
            array[i] = BitConverter.ToInt16(itemData, i * 2);
        }
        return array;
    }

    public void SetInt16Array(short[] array)
    {
        itemType = ItemizeType.ShortArray;
        itemData = new byte[array.Length * 2];
        byte[] array2 = new byte[2];
        for (int i = 0; i < array.Length; i++)
        {
            array2 = BitConverter.GetBytes(array[i]);
            Array.Copy(array2, 0, itemData, i * 2, array2.Length);
        }
    }

    public void SetInt16Array(string name, short[] array)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetInt16Array(text2, array);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetInt16Array(text2, array);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetInt16Array(array);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetInt16Array(array);
        }
    }

    public short[] GetInt16Array(string name, short[] defaultValues)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetInt16Array();
            }
            return defaultValues;
        }
        catch
        {
            return defaultValues;
        }
    }

    public bool GetInt16Array(string name, ref short[] values)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            values = itemize.GetInt16Array();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int[] GetInt32Array()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        int num = itemData.Length / 4;
        int[] array = new int[num];
        for (int i = 0; i < num; i++)
        {
            array[i] = BitConverter.ToInt32(itemData, i * 4);
        }
        return array;
    }

    public int[] GetInt32ArrayExc()
    {
        if (itemData == null)
        {
            throw new ApplicationException("itemizeが見つかりません");
        }
        int num = itemData.Length / 4;
        int[] array = new int[num];
        for (int i = 0; i < num; i++)
        {
            array[i] = BitConverter.ToInt32(itemData, i * 4);
        }
        return array;
    }

    public void SetInt32Array(int[] array)
    {
        itemType = ItemizeType.LongArray;
        itemData = new byte[array.Length * 4];
        byte[] array2 = new byte[4];
        for (int i = 0; i < array.Length; i++)
        {
            array2 = BitConverter.GetBytes(array[i]);
            Array.Copy(array2, 0, itemData, i * 4, array2.Length);
        }
    }

    public void SetInt32Array(string name, int[] array)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetInt32Array(text2, array);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetInt32Array(text2, array);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetInt32Array(array);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetInt32Array(array);
            AddItem(itemize2);
        }
    }

    public int[] GetInt32Array(string name, int[] defaultValues)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetInt32Array();
            }
            return defaultValues;
        }
        catch
        {
            return defaultValues;
        }
    }

    public bool GetInt32Array(string name, ref int[] values)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            values = itemize.GetInt32Array();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void GetInt32ArrayExc(string name, ref int[] values)
    {
        Itemize itemize = (Itemize)children[name];
        values = itemize.GetInt32ArrayExc();
    }

    public float[] GetSingleArray()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        int num = itemData.Length / 4;
        float[] array = new float[num];
        for (int i = 0; i < num; i++)
        {
            array[i] = BitConverter.ToSingle(itemData, i * 4);
        }
        return array;
    }

    public void SetSingleArray(float[] array)
    {
        itemType = ItemizeType.FloatArray;
        itemData = new byte[array.Length * 4];
        byte[] array2 = new byte[4];
        for (int i = 0; i < array.Length; i++)
        {
            array2 = BitConverter.GetBytes(array[i]);
            Array.Copy(array2, 0, itemData, i * 4, array2.Length);
        }
    }

    public void SetSingleArray(string name, float[] array)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetSingleArray(text2, array);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetSingleArray(text2, array);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetSingleArray(array);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetSingleArray(array);
            AddItem(itemize2);
        }
    }

    public float[] GetSingleArray(string name, float[] defaultValues)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetSingleArray();
            }
            return defaultValues;
        }
        catch
        {
            return defaultValues;
        }
    }

    public bool GetSingleArray(string name, ref float[] values)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            values = itemize.GetSingleArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public float[] GetSingleArray(string name)
    {
        return ((Itemize)children[name]).GetSingleArray();
    }

    public double[] GetDoubleArray()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        int num = itemData.Length / 8;
        double[] array = new double[num];
        for (int i = 0; i < num; i++)
        {
            array[i] = BitConverter.ToDouble(itemData, i * 8);
        }
        return array;
    }

    public void SetDoubleArray(double[] array)
    {
        itemType = ItemizeType.DoubleArray;
        itemData = new byte[array.Length * 8];
        byte[] array2 = new byte[8];
        for (int i = 0; i < array.Length; i++)
        {
            array2 = BitConverter.GetBytes(array[i]);
            Array.Copy(array2, 0, itemData, i * 8, array2.Length);
        }
    }

    public void SetDoubleArray(string name, double[] array)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetDoubleArray(text2, array);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetDoubleArray(text2, array);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetDoubleArray(array);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetDoubleArray(array);
            AddItem(itemize2);
        }
    }

    public double[] GetDoubleArray(string name, double[] defaultValues)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize != null)
            {
                return itemize.GetDoubleArray();
            }
            return defaultValues;
        }
        catch
        {
            return defaultValues;
        }
    }

    public bool GetDoubleArray(string name, ref double[] values)
    {
        try
        {
            Itemize itemize = (Itemize)children[name];
            if (itemize == null)
            {
                return false;
            }
            values = itemize.GetDoubleArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Itemize GetChildItem(int no)
    {
        if (no >= 0 && no < children.Count)
        {
            return (Itemize)children[no];
        }
        throw new ArgumentException($"Itemize.cs::GetChildItem 引数({no})が不正です。0<= & {children.Count}以下でなければなりません\n");
    }

    public void GetFloatInfoExc(ref float Default, ref float Min, ref float Max, ref float Step, ref string Format, ref string Unit)
    {
        if (!GetFloatInfo(ref Default, ref Min, ref Max, ref Step, ref Format, ref Unit))
        {
            throw new InvalidOperationException("FloatInfoではないオブジェクトにGetFloatは実行できません");
        }
    }

    public bool GetFloatInfo(ref float Default, ref float Min, ref float Max, ref float Step, ref string Format, ref string Unit)
    {
        if (itemType == ItemizeType.FloatInfo)
        {
            byte[] byteArray = GetByteArray();
            if (byteArray.Length != 0)
            {
                Default = BitConverter.ToSingle(byteArray, 0);
                Min = BitConverter.ToSingle(byteArray, 4);
                Max = BitConverter.ToSingle(byteArray, 8);
                Step = BitConverter.ToSingle(byteArray, 12);
                int num = 0;
                for (num = 1; num < 15 && byteArray[16 + num] != 0; num++)
                {
                }
                Format = Encoding.ASCII.GetString(byteArray, 16, num);
                for (num = 1; num < 15 && byteArray[32 + num] != 0; num++)
                {
                }
                Unit = Encoding.ASCII.GetString(byteArray, 32, num);
                return true;
            }
        }
        return false;
    }

    public bool GetFloatInfo(string name, ref float Default, ref float Min, ref float Max, ref float Step, ref string Format, ref string Unit)
    {
        try
        {
            return ((Itemize)children[name]).GetFloatInfo(ref Default, ref Min, ref Max, ref Step, ref Format, ref Unit);
        }
        catch
        {
            return false;
        }
    }

    public void SetFloatInfo(float Default, float Min, float Max, float Step, string Format, string Unit)
    {
        itemType = ItemizeType.FloatInfo;
        _ = new byte[16];
        itemData = new byte[48];
        Array.Copy(BitConverter.GetBytes(Default), 0, itemData, 0, 4);
        Array.Copy(BitConverter.GetBytes(Min), 0, itemData, 4, 4);
        Array.Copy(BitConverter.GetBytes(Max), 0, itemData, 8, 4);
        Array.Copy(BitConverter.GetBytes(Step), 0, itemData, 12, 4);
        Format += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Format), 0, itemData, 16, Format.Length);
        Unit += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Unit), 0, itemData, 32, Unit.Length);
    }

    public void SetFloatInfo(string name, float Default, float Min, float Max, float Step, string Format, string Unit)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetFloatInfo(text2, Default, Min, Max, Step, Format, Unit);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetFloatInfo(text2, Default, Min, Max, Step, Format, Unit);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetFloatInfo(Default, Min, Max, Step, Format, Unit);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetFloatInfo(Default, Min, Max, Step, Format, Unit);
            AddItem(itemize2);
        }
    }

    public bool GetLongInfo(ref int Default, ref int Min, ref int Max, ref int Step, ref string Format, ref string Unit)
    {
        if (itemType == ItemizeType.LongInfo)
        {
            byte[] byteArray = GetByteArray();
            if (byteArray.Length != 0)
            {
                Default = BitConverter.ToInt32(byteArray, 0);
                Min = BitConverter.ToInt32(byteArray, 4);
                Max = BitConverter.ToInt32(byteArray, 8);
                Step = BitConverter.ToInt32(byteArray, 12);
                int num = 0;
                for (num = 1; num < 15 && byteArray[16 + num] != 0; num++)
                {
                }
                Format = Encoding.ASCII.GetString(byteArray, 16, num);
                for (num = 1; num < 15 && byteArray[32 + num] != 0; num++)
                {
                }
                Unit = Encoding.ASCII.GetString(byteArray, 32, num);
                return true;
            }
        }
        return false;
    }

    public bool GetLongInfo(string name, ref int Default, ref int Min, ref int Max, ref int Step, ref string Format, ref string Unit)
    {
        try
        {
            return ((Itemize)children[name]).GetLongInfo(ref Default, ref Min, ref Max, ref Step, ref Format, ref Unit);
        }
        catch
        {
            return false;
        }
    }

    public void SetLongInfo(int Default, int Min, int Max, int Step, string Format, string Unit)
    {
        itemType = ItemizeType.LongInfo;
        _ = new byte[16];
        itemData = new byte[48];
        Array.Copy(BitConverter.GetBytes(Default), 0, itemData, 0, 4);
        Array.Copy(BitConverter.GetBytes(Min), 0, itemData, 4, 4);
        Array.Copy(BitConverter.GetBytes(Max), 0, itemData, 8, 4);
        Array.Copy(BitConverter.GetBytes(Step), 0, itemData, 12, 4);
        Format += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Format), 0, itemData, 16, Format.Length);
        Unit += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Unit), 0, itemData, 32, Unit.Length);
    }

    public void SetLongInfo(string name, int Default, int Min, int Max, int Step, string Format, string Unit)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetLongInfo(text2, Default, Min, Max, Step, Format, Unit);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetLongInfo(text2, Default, Min, Max, Step, Format, Unit);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetLongInfo(Default, Min, Max, Step, Format, Unit);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetLongInfo(Default, Min, Max, Step, Format, Unit);
            AddItem(itemize2);
        }
    }

    public bool GetDoubleInfo(ref double Default, ref double Min, ref double Max, ref double Step, ref string Format, ref string Unit)
    {
        if (itemType == ItemizeType.DoubleInfo)
        {
            byte[] byteArray = GetByteArray();
            if (byteArray.Length != 0)
            {
                Default = BitConverter.ToDouble(byteArray, 0);
                Min = BitConverter.ToDouble(byteArray, 8);
                Max = BitConverter.ToDouble(byteArray, 16);
                Step = BitConverter.ToDouble(byteArray, 24);
                int num = 0;
                for (num = 1; num < 15 && byteArray[32 + num] != 0; num++)
                {
                }
                Format = Encoding.ASCII.GetString(byteArray, 32, num);
                for (num = 1; num < 15 && byteArray[48 + num] != 0; num++)
                {
                }
                Unit = Encoding.ASCII.GetString(byteArray, 48, num);
                return true;
            }
        }
        return false;
    }

    public bool GetDoubleInfo(string name, ref double Default, ref double Min, ref double Max, ref double Step, ref string Format, ref string Unit)
    {
        try
        {
            return ((Itemize)children[name]).GetDoubleInfo(ref Default, ref Min, ref Max, ref Step, ref Format, ref Unit);
        }
        catch
        {
            return false;
        }
    }

    public void SetDoubleInfo(double Default, double Min, double Max, double Step, string Format, string Unit)
    {
        itemType = ItemizeType.DoubleInfo;
        _ = new byte[16];
        itemData = new byte[64];
        Array.Copy(BitConverter.GetBytes(Default), 0, itemData, 0, 8);
        Array.Copy(BitConverter.GetBytes(Min), 0, itemData, 8, 8);
        Array.Copy(BitConverter.GetBytes(Max), 0, itemData, 16, 8);
        Array.Copy(BitConverter.GetBytes(Step), 0, itemData, 24, 8);
        Format += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Format), 0, itemData, 32, Format.Length);
        Unit += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Unit), 0, itemData, 48, Unit.Length);
    }

    public void SetDoubleInfo(string name, double Default, double Min, double Max, double Step, string Format, string Unit)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetDoubleInfo(text2, Default, Min, Max, Step, Format, Unit);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetDoubleInfo(text2, Default, Min, Max, Step, Format, Unit);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetDoubleInfo(Default, Min, Max, Step, Format, Unit);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetDoubleInfo(Default, Min, Max, Step, Format, Unit);
            AddItem(itemize2);
        }
    }

    public bool GetShortInfo(ref short Default, ref short Min, ref short Max, ref short Step, ref string Format, ref string Unit)
    {
        if (itemType == ItemizeType.ShortInfo)
        {
            byte[] byteArray = GetByteArray();
            if (byteArray.Length != 0)
            {
                Default = BitConverter.ToInt16(byteArray, 0);
                Min = BitConverter.ToInt16(byteArray, 2);
                Max = BitConverter.ToInt16(byteArray, 4);
                Step = BitConverter.ToInt16(byteArray, 6);
                int num = 0;
                for (num = 1; num < 15 && byteArray[8 + num] != 0; num++)
                {
                }
                Format = Encoding.ASCII.GetString(byteArray, 8, num);
                for (num = 1; num < 15 && byteArray[24 + num] != 0; num++)
                {
                }
                Unit = Encoding.ASCII.GetString(byteArray, 24, num);
                return true;
            }
        }
        return false;
    }

    public bool GetShortInfo(string name, ref short Default, ref short Min, ref short Max, ref short Step, ref string Format, ref string Unit)
    {
        try
        {
            return ((Itemize)children[name]).GetShortInfo(ref Default, ref Min, ref Max, ref Step, ref Format, ref Unit);
        }
        catch
        {
            return false;
        }
    }

    public void SetShortInfo(short Default, short Min, short Max, short Step, string Format, string Unit)
    {
        itemType = ItemizeType.ShortInfo;
        _ = new byte[16];
        itemData = new byte[40];
        Array.Copy(BitConverter.GetBytes(Default), 0, itemData, 0, 2);
        Array.Copy(BitConverter.GetBytes(Min), 0, itemData, 2, 2);
        Array.Copy(BitConverter.GetBytes(Max), 0, itemData, 4, 2);
        Array.Copy(BitConverter.GetBytes(Step), 0, itemData, 6, 2);
        Format += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Format), 0, itemData, 8, Format.Length);
        Unit += "\0";
        Array.Copy(Encoding.GetEncoding("Shift-JIS").GetBytes(Unit), 0, itemData, 24, Unit.Length);
    }

    public void SetShortInfo(string name, short Default, short Min, short Max, short Step, string Format, string Unit)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetShortInfo(text2, Default, Min, Max, Step, Format, Unit);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetShortInfo(text2, Default, Min, Max, Step, Format, Unit);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetShortInfo(Default, Min, Max, Step, Format, Unit);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetShortInfo(Default, Min, Max, Step, Format, Unit);
            AddItem(itemize2);
        }
    }

    public bool GetMultiString(ref string[] strArray)
    {
        if (itemType == ItemizeType.MultiString)
        {
            strArray = GetStringArray();
            return true;
        }
        return false;
    }

    public bool GetMultiString(string name, ref string[] strArra)
    {
        try
        {
            int num = -1;
            string text = "";
            string text2 = "";
            if ((num = name.IndexOf("\\")) != -1)
            {
                text = name.Substring(0, num);
                num++;
                text2 = name.Substring(num);
                if (children.Contains(text))
                {
                    return ((Itemize)children[text]).GetMultiString(text2, ref strArra);
                }
            }
            else if (Contain(name))
            {
                return GetItemExc(name).GetMultiString(ref strArra);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void SetMultiString(string[] strArray)
    {
        SetStringArray(strArray);
    }

    public void SetMultiString(string name, string[] strArray)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetMultiString(text2, strArray);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetMultiString(text2, strArray);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetMultiString(strArray);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetMultiString(strArray);
            AddItem(itemize2);
        }
    }

    public void SetSelectableItem(short value)
    {
        itemType = ItemizeType.Selectable;
        itemData = BitConverter.GetBytes(value);
    }

    public void SetSelectableItem(string name, short value)
    {
        int num = -1;
        string text = "";
        string text2 = "";
        if ((num = name.IndexOf("\\")) != -1)
        {
            text = name.Substring(0, num);
            num++;
            text2 = name.Substring(num);
            if (children.Contains(text))
            {
                ((Itemize)children[text]).SetSelectableItem(text2, value);
                return;
            }
            Itemize itemize = new Itemize();
            itemize.itemType = ItemizeType.Unknown;
            itemize.itemName = text;
            itemize.SetSelectableItem(text2, value);
            AddItem(itemize);
        }
        else if (Contain(name))
        {
            GetItemExc(name).SetSelectableItem(value);
        }
        else
        {
            Itemize itemize2 = new Itemize();
            itemize2.SetName(name);
            itemize2.SetSelectableItem(value);
            AddItem(itemize2);
        }
    }

    public short GetSelectable()
    {
        if (itemData == null)
        {
            throw new InvalidCastException();
        }
        return BitConverter.ToInt16(itemData, 0);
    }

    public bool GetSelectableItem(string itemname, ref short nData)
    {
        if (itemType == ItemizeType.Selectable)
        {
            try
            {
                Itemize itemize = (Itemize)children[Name];
                if (itemize == null)
                {
                    return false;
                }
                nData = itemize.GetSelectable();
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public short GetSelectableItem(string itemname, short defaultValue)
    {
        try
        {
            return ((Itemize)children[Name])?.GetSelectable() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public bool Contain(string itemname)
    {
        int num = -1;
        string text = "";
        string empty = string.Empty;
        if ((num = itemname.IndexOf("\\")) != -1)
        {
            text = itemname.Substring(0, num);
            num++;
            empty = itemname.Substring(num);
            return ((Itemize)children[text]).Contain(empty);
        }
        return children.Contains(itemname);
    }

    public string ConvertToString()
    {
        return "";
    }

    public void SetMemBlock(byte[] mb)
    {
        string memBlock = encode.GetString(mb, 0, mb.Length).TrimEnd(default(char));
        SetMemBlock(memBlock);
    }

    public void SetMemBlock(string mb)
    {
        if (mb.LastIndexOf('\t') != mb.Length - 1)
        {
            mb += "\t";
        }
        int num = 0;
        int num2 = 0;
        int num3 = 0;
        int num4 = 0;
        int num5 = 0;
        string text = "";
        string text2 = "";
        string text3 = "";
        string[] array = null;
        byte[] array2 = null;
        short[] array3 = null;
        int[] array4 = null;
        float[] array5 = null;
        double[] array6 = null;
        short num6 = 0;
        short num7 = 0;
        short num8 = 0;
        short num9 = 0;
        int num10 = 0;
        int num11 = 0;
        int num12 = 0;
        int num13 = 0;
        float num14 = 0f;
        float num15 = 0f;
        float num16 = 0f;
        float num17 = 0f;
        double num18 = 0.0;
        double num19 = 0.0;
        double num20 = 0.0;
        double num21 = 0.0;
        num2 = mb.IndexOf('\t', num);
        if (num2 <= -1)
        {
            return;
        }
        string text4 = mb.Substring(num, num2 - num);
        num = num2 + 1;
        num3 = (num4 = 0);
        num3++;
        text = "";
        num4 = text4.IndexOf(':', num3);
        text = text4.Substring(num3, num4 - num3);
        num3 = num4 + 1;
        num5 = text.Length + 1;
        num3 += 2;
        text2 = "";
        num4 = text4.IndexOf(':', num3);
        text2 = text4.Substring(num3, num4 - num3);
        num3 = num4 + 1;
        text3 = "";
        text3 = text4.Substring(num3);
        switch ((ItemizeType)Array.IndexOf(_data_type, text2))
        {
            case ItemizeType.Unknown:
                itemName = text;
                itemType = ItemizeType.Unknown;
                break;
            case ItemizeType.Char:
                SetChar(Convert.ToChar(text3));
                break;
            case ItemizeType.Short:
                SetInt16(Convert.ToInt16(text3));
                break;
            case ItemizeType.Long:
                SetInt32(Convert.ToInt32(text3));
                break;
            case ItemizeType.Float:
                SetSingle(Convert.ToSingle(text3));
                break;
            case ItemizeType.Double:
                SetDouble(Convert.ToDouble(text3));
                break;
            case ItemizeType.CharArray:
                {
                    array = text3.Split(',');
                    array2 = new byte[array.Length];
                    for (int j = 0; j < array.Length; j++)
                    {
                        array2[j] = Convert.ToByte(array[j]);
                    }
                    SetByteArray(array2);
                    array2 = null;
                    break;
                }
            case ItemizeType.ShortArray:
                {
                    array = text3.Split(',');
                    array3 = new short[array.Length];
                    for (int m = 0; m < array.Length; m++)
                    {
                        array3[m] = Convert.ToInt16(array[m]);
                    }
                    SetInt16Array(array3);
                    array3 = null;
                    break;
                }
            case ItemizeType.LongArray:
                {
                    array = text3.Split(',');
                    array4 = new int[array.Length];
                    for (int l = 0; l < array.Length; l++)
                    {
                        array4[l] = Convert.ToInt32(array[l]);
                    }
                    SetInt32Array(array4);
                    array3 = null;
                    break;
                }
            case ItemizeType.FloatArray:
                {
                    array = text3.Split(',');
                    array5 = new float[array.Length];
                    for (int k = 0; k < array.Length; k++)
                    {
                        array5[k] = Convert.ToSingle(array[k]);
                    }
                    SetSingleArray(array5);
                    array5 = null;
                    break;
                }
            case ItemizeType.DoubleArray:
                {
                    array = text3.Split(',');
                    array6 = new double[array.Length];
                    for (int i = 0; i < array.Length; i++)
                    {
                        array6[i] = Convert.ToDouble(array[i]);
                    }
                    SetDoubleArray(array6);
                    array6 = null;
                    break;
                }
            case ItemizeType.Bool:
                SetBoolean(Convert.ToBoolean(text3));
                break;
            case ItemizeType.String:
                SetString(text3);
                break;
            case ItemizeType.MultiString:
                array = text3.Replace("\r\n", "n").Split('\n');
                SetMultiString(array);
                array = null;
                break;
            case ItemizeType.Selectable:
                SetSelectableItem(text, Convert.ToInt16(text3));
                break;
            case ItemizeType.ShortInfo:
                array = text3.Split(',');
                num6 = Convert.ToInt16(array[0]);
                num7 = Convert.ToInt16(array[1]);
                num8 = Convert.ToInt16(array[2]);
                num9 = Convert.ToInt16(array[3]);
                SetShortInfo(text, num6, num7, num8, num9, array[4], array[5]);
                break;
            case ItemizeType.LongInfo:
                array = text3.Split(',');
                num10 = Convert.ToInt32(array[0]);
                num11 = Convert.ToInt32(array[1]);
                num12 = Convert.ToInt32(array[2]);
                num13 = Convert.ToInt32(array[3]);
                SetLongInfo(text, num10, num11, num12, num13, array[4], array[5]);
                break;
            case ItemizeType.FloatInfo:
                array = text3.Split(',');
                num14 = Convert.ToSingle(array[0]);
                num15 = Convert.ToSingle(array[1]);
                num16 = Convert.ToSingle(array[2]);
                num17 = Convert.ToSingle(array[3]);
                SetFloatInfo(text, num14, num15, num16, num17, array[4], array[5]);
                break;
            case ItemizeType.DoubleInfo:
                array = text3.Split(',');
                num18 = Convert.ToDouble(array[0]);
                num19 = Convert.ToDouble(array[1]);
                num20 = Convert.ToDouble(array[2]);
                num21 = Convert.ToDouble(array[3]);
                SetDoubleInfo(text, num18, num19, num20, num21, array[4], array[5]);
                break;
        }
        for (num2 = mb.IndexOf('\t', num); num2 != -1; num2 = mb.IndexOf('\t', num))
        {
            string text5 = mb.Substring(num, num2 - num);
            num = num2 + 1;
            num3 = (num4 = 0);
            num3++;
            text = "";
            num4 = text5.IndexOf(':', num3);
            text = text5.Substring(num3 + num5, num4 - num3 - num5);
            num3 = num4 + 1;
            num3 += 2;
            text2 = "";
            num4 = text5.IndexOf(':', num3);
            text2 = text5.Substring(num3, num4 - num3);
            num3 = num4 + 1;
            text3 = "";
            text3 = text5.Substring(num3);
            switch ((ItemizeType)Array.IndexOf(_data_type, text2))
            {
                case ItemizeType.Unknown:
                    SetItem(text, ItemizeType.Unknown);
                    break;
                case ItemizeType.Char:
                    SetChar(text, Convert.ToChar(text3));
                    break;
                case ItemizeType.Short:
                    SetInt16(text, Convert.ToInt16(text3));
                    break;
                case ItemizeType.Long:
                    SetInt32(text, Convert.ToInt32(text3));
                    break;
                case ItemizeType.Float:
                    SetSingle(text, Convert.ToSingle(text3));
                    break;
                case ItemizeType.Double:
                    SetDouble(text, Convert.ToDouble(text3));
                    break;
                case ItemizeType.CharArray:
                    {
                        array = text3.Split(',');
                        array2 = new byte[array.Length];
                        for (int num22 = 0; num22 < array.Length; num22++)
                        {
                            array2[num22] = Convert.ToByte(array[num22]);
                        }
                        SetByteArray(text, array2);
                        array2 = null;
                        break;
                    }
                case ItemizeType.ShortArray:
                    {
                        array = text3.Split(',');
                        array3 = new short[array.Length];
                        for (int num25 = 0; num25 < array.Length; num25++)
                        {
                            array3[num25] = Convert.ToInt16(array[num25]);
                        }
                        SetInt16Array(text, array3);
                        array3 = null;
                        break;
                    }
                case ItemizeType.LongArray:
                    {
                        array = text3.Split(',');
                        array4 = new int[array.Length];
                        for (int num24 = 0; num24 < array.Length; num24++)
                        {
                            array4[num24] = Convert.ToInt32(array[num24]);
                        }
                        SetInt32Array(text, array4);
                        array4 = null;
                        break;
                    }
                case ItemizeType.FloatArray:
                    {
                        array = text3.Split(',');
                        array5 = new float[array.Length];
                        for (int num23 = 0; num23 < array.Length; num23++)
                        {
                            array5[num23] = Convert.ToSingle(array[num23]);
                        }
                        SetSingleArray(text, array5);
                        array5 = null;
                        break;
                    }
                case ItemizeType.DoubleArray:
                    {
                        array = text3.Split(',');
                        array6 = new double[array.Length];
                        for (int n = 0; n < array.Length; n++)
                        {
                            array6[n] = Convert.ToDouble(array[n]);
                        }
                        SetDoubleArray(text, array6);
                        array6 = null;
                        break;
                    }
                case ItemizeType.Bool:
                    SetBoolean(text, Convert.ToBoolean(text3));
                    break;
                case ItemizeType.String:
                    SetString(text, text3);
                    break;
                case ItemizeType.MultiString:
                    array = text3.Replace("\r\n", "n").Split('\n');
                    SetMultiString(text, array);
                    array = null;
                    break;
                case ItemizeType.Selectable:
                    SetSelectableItem(text, Convert.ToInt16(text3));
                    break;
                case ItemizeType.ShortInfo:
                    array = text3.Split(',');
                    num6 = Convert.ToInt16(array[0]);
                    num7 = Convert.ToInt16(array[1]);
                    num8 = Convert.ToInt16(array[2]);
                    num9 = Convert.ToInt16(array[3]);
                    SetShortInfo(text, num6, num7, num8, num9, array[4], array[5]);
                    break;
                case ItemizeType.LongInfo:
                    array = text3.Split(',');
                    num10 = Convert.ToInt32(array[0]);
                    num11 = Convert.ToInt32(array[1]);
                    num12 = Convert.ToInt32(array[2]);
                    num13 = Convert.ToInt32(array[3]);
                    SetLongInfo(text, num10, num11, num12, num13, array[4], array[5]);
                    break;
                case ItemizeType.FloatInfo:
                    array = text3.Split(',');
                    num14 = Convert.ToSingle(array[0]);
                    num15 = Convert.ToSingle(array[1]);
                    num16 = Convert.ToSingle(array[2]);
                    num17 = Convert.ToSingle(array[3]);
                    SetFloatInfo(text, num14, num15, num16, num17, array[4], array[5]);
                    break;
                case ItemizeType.DoubleInfo:
                    array = text3.Split(',');
                    num18 = Convert.ToDouble(array[0]);
                    num19 = Convert.ToDouble(array[1]);
                    num20 = Convert.ToDouble(array[2]);
                    num21 = Convert.ToDouble(array[3]);
                    SetDoubleInfo(text, num18, num19, num20, num21, array[4], array[5]);
                    break;
            }
        }
    }

    public Itemize FindItem(string key)
    {
        if (Name.Equals(key))
        {
            return this;
        }
        Itemize itemize = null;
        foreach (Itemize value in children.Values)
        {
            if (value.Name.Equals(key))
            {
                itemize = value;
                break;
            }
            if (value.ChildNum > 0)
            {
                itemize = value.FindItem(key);
                if (itemize != null)
                {
                    break;
                }
            }
        }
        return itemize;
    }

    public Itemize FindItemExc(string key)
    {
        return FindItem(key) ?? throw new ArgumentException($"Itemize内にkey:{key}は見つかりませんでした");
    }

    public string ToTreeString()
    {
        return ToTreeString(null, null);
    }

    private string ToTreeString(string name, StringBuilder sb)
    {
        if (sb == null)
        {
            sb = new StringBuilder();
        }
        name = ((name != null) ? (name + "." + itemName) : itemName);
        sb.Append(name);
        if (ToString() == string.Empty)
        {
            sb.Append("\n");
        }
        else
        {
            sb.Append(" = " + ToString() + "(" + itemTypeString() + ")\n");
        }
        for (int i = 0; i < children.Count; i++)
        {
            ((Itemize)children[i]).ToTreeString(name, sb);
        }
        return sb.ToString();
    }

    private string itemTypeString()
    {
        string empty = string.Empty;
        try
        {
            return itemType switch
            {
                ItemizeType.Unknown => "Unknown",
                ItemizeType.String => "String",
                ItemizeType.Char => "Char",
                ItemizeType.Bool => "Bool",
                ItemizeType.Short => "Int16",
                ItemizeType.Long => "Int32",
                ItemizeType.Float => "Float",
                ItemizeType.Double => "Double",
                ItemizeType.Selectable => "Selectable",
                ItemizeType.MultiString => "MultiString",
                ItemizeType.ShortArray => "Int16Array",
                ItemizeType.LongArray => "Int32Array",
                ItemizeType.FloatArray => "FloatArray",
                ItemizeType.DoubleArray => "DoubleArray",
                _ => "Unknown",
            };
        }
        catch
        {
            return empty;
        }
    }

    public List<Itemize> GetFirstItems(string baseName)
    {
        List<Itemize> list = new List<Itemize>();
        for (int i = 0; i < children.Count; i++)
        {
            Itemize itemize = (Itemize)children[i];
            if (string.Compare(baseName, 0, itemize.Name, 0, baseName.Length) == 0)
            {
                list.Add(itemize);
            }
        }
        if (list.Count == 0)
        {
            return null;
        }
        return list;
    }
}
