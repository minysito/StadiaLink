using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
namespace StadiaStudio {
 public sealed class LanguageOption {public string Code{get;private set;}public string Name{get;private set;}public string Badge{get{return Code.ToUpperInvariant();}}public LanguageOption(string code,string name){Code=code;Name=name;}public override string ToString(){return Badge+"   "+Name;}}
 public static class I18n {
  public static readonly LanguageOption[] Options={new LanguageOption("es","Español"),new LanguageOption("en","English"),new LanguageOption("fr","Français"),new LanguageOption("de","Deutsch")};
  static readonly Dictionary<string,string[]> catalog=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string,string> cache=new Dictionary<string,string>();static string[] phrases;
  public static string Code {get;private set;}
  static readonly DependencyProperty SourceProperty=DependencyProperty.RegisterAttached("TranslationSource",typeof(string),typeof(I18n));
  static readonly DependencyProperty RawProperty=DependencyProperty.RegisterAttached("RawText",typeof(bool),typeof(I18n),new PropertyMetadata(false));
  static I18n(){using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("locales.tsv"))using(StreamReader reader=new StreamReader(stream)){string line;while((line=reader.ReadLine())!=null){string[] fields=line.Split('|');if(fields.Length==4)catalog[fields[0]]=fields;}}phrases=catalog.Keys.OrderByDescending(k=>k.Length).ToArray();Code=Saved;}
  public static string Saved {get{using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\StadiaStudio")){string code=key==null?"es":key.GetValue("Language","es") as string;return Options.Any(o=>o.Code==code)?code:"es";}}}
  public static void Set(string code){if(!Options.Any(o=>o.Code==code))throw new ArgumentException("Unknown language");Code=code;cache.Clear();}
  public static void Save(){using(RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\StadiaStudio"))key.SetValue("Language",Code);}
  public static string T(string source){if(source==null||Code=="es")return source;string translated;if(cache.TryGetValue(source,out translated))return translated;int column=Code=="en"?1:Code=="fr"?2:3;string[] values;if(catalog.TryGetValue(source,out values)){translated=values[column];if(source.Any(Char.IsLetter)&&source==source.ToUpperInvariant())translated=translated.ToUpperInvariant();}else{translated=source;foreach(string phrase in phrases){if(phrase.Length>=4&&translated.Contains(phrase))translated=translated.Replace(phrase,catalog[phrase][column]);}}if(cache.Count>2048)cache.Clear();cache[source]=translated;return translated;}
  public static void Remember(DependencyObject target,string source){target.SetValue(SourceProperty,source);}
  public static void Raw(DependencyObject target){target.SetValue(RawProperty,true);}
  public static DataTemplate LanguageTemplate(){var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);var badge=new FrameworkElementFactory(typeof(Border));badge.SetValue(Border.BackgroundProperty,Ui.Brush("#2C3A2D"));badge.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));badge.SetValue(Border.WidthProperty,30.0);badge.SetValue(Border.HeightProperty,22.0);var code=new FrameworkElementFactory(typeof(TextBlock));code.SetBinding(TextBlock.TextProperty,new Binding("Badge"));code.SetValue(TextBlock.FontSizeProperty,10.0);code.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);code.SetValue(TextBlock.ForegroundProperty,Ui.Lime);code.SetValue(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Center);code.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);badge.AppendChild(code);row.AppendChild(badge);var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Name"));name.SetValue(TextBlock.MarginProperty,new Thickness(10,0,0,0));name.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(name);return new DataTemplate{VisualTree=row};}
  public static void SetText(TextBlock target,string source){if((string)target.GetValue(SourceProperty)!=source)Remember(target,source);string translated=T(source);if(target.Text!=translated)target.Text=translated;}
  public static void TranslateTree(DependencyObject root){if(root==null||(bool)root.GetValue(RawProperty))return;TextBlock text=root as TextBlock;ContentControl control=root as ContentControl;if(text!=null){string source=(string)text.GetValue(SourceProperty);if(source==null){source=text.Text;Remember(text,source);}text.Text=T(source);}else if(control!=null&&control.Content is string){string source=(string)control.GetValue(SourceProperty);if(source==null){source=(string)control.Content;Remember(control,source);}control.Content=T(source);}foreach(object child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject)TranslateTree((DependencyObject)child);}
 }
}
