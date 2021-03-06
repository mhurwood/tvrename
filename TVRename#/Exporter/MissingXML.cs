using System;
using System.Xml;

namespace TVRename
{
    class MissingXML : MissingExporter
    {
        public override bool Active() =>TVSettings.Instance.ExportMissingXML;
        protected override string Location() =>TVSettings.Instance.ExportMissingXMLTo;
        
        public override void Run(ItemList TheActionList)
        {
            if (TVSettings.Instance.ExportMissingXML)
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                //XmlWriterSettings settings = gcnew XmlWriterSettings();
                settings.Indent = true;
                settings.NewLineOnAttributes = true;
                using (XmlWriter writer = XmlWriter.Create(Location(), settings))
                {
                    writer.WriteStartDocument();
                    
                    writer.WriteStartElement("TVRename");
                    XMLHelper.WriteAttributeToXML(writer,"Version","2.1");
                    writer.WriteStartElement("MissingItems");

                    foreach (Item Action in TheActionList)
                    {
                        if (Action is ItemMissing)
                        {
                            ItemMissing Missing = (ItemMissing)(Action);
                            writer.WriteStartElement("MissingItem");

                            XMLHelper.WriteElementToXML(writer,"id",Missing.Episode.SI.TVDBCode);
                            XMLHelper.WriteElementToXML(writer, "title",Missing.Episode.TheSeries.Name);
                            XMLHelper.WriteElementToXML(writer, "season", Helpers.pad(Missing.Episode.AppropriateSeasonNumber));
                            XMLHelper.WriteElementToXML(writer, "episode", Helpers.pad(Missing.Episode.AppropriateEpNum));
                            XMLHelper.WriteElementToXML(writer, "episodeName",Missing.Episode.Name);
                            XMLHelper.WriteElementToXML(writer, "description",Missing.Episode.Overview);

                            writer.WriteStartElement("pubDate");
                            DateTime? dt = Missing.Episode.GetAirDateDT(true);
                            if (dt != null)
                                writer.WriteValue(dt.Value.ToString("F"));
                            writer.WriteEndElement();
                            
                            writer.WriteEndElement(); // MissingItem

                        }
                    }
                    writer.WriteEndElement(); // MissingItems
                    writer.WriteEndElement(); // tvrename
                    writer.WriteEndDocument();
                }
            }
        }
    }
}
