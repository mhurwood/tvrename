// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 
using System.Xml;

// An "IgnoreItem" represents a file/episode to never ask the user about again. (Right-click->Ignore Selected / Options->Ignore List)

namespace TVRename
{
    public class IgnoreItem
    {
        public string FileAndPath;

        public IgnoreItem(XmlReader r)
        {
            if (r.Name == "Ignore")
                this.FileAndPath = r.ReadElementContentAsString();
        }

        public IgnoreItem(string fileAndPath)
        {
            this.FileAndPath = fileAndPath;
        }

        public bool SameFileAs(IgnoreItem o)
        {
            if (string.IsNullOrEmpty(this.FileAndPath) || string.IsNullOrEmpty(o?.FileAndPath))
                return false;
            return this.FileAndPath == o.FileAndPath;
        }

        public void Write(XmlWriter writer)
        {
            XMLHelper.WriteElementToXML(writer,"Ignore",this.FileAndPath);
        }
    }
}
