<#
.SYNOPSIS
Assigns semantic WPF automation IDs after layout stabilization.
.DESCRIPTION
Identifiers use bindings, handler names, and command parameters rather than visual-tree positions.
Run explicitly after adding controls; review changes before commit. Existing identifiers are preserved.
#>
$ErrorActionPreference = 'Stop'
$accessibleLabels = @{
    'CustomXSlider'='EXIFの横位置（%）'; 'CustomYSlider'='EXIFの縦位置（%）';
    'Export.FrameWidth'='出力の枠幅'; 'Export.OutputFormat'='一括出力形式'; 'Export.Quality'='JPEG品質';
    'Export.RotationDegrees'='出力の回転'; 'FilterDateFrom'='撮影日の開始'; 'FilterDateTo'='撮影日の終了';
    'FilterFileType'='写真の種類'; 'FilterMinRating'='評価の下限'; 'FilterNameSearch'='ファイル名で絞り込み';
    'FrameWidthSlider'='枠の幅'; 'IsFilterPanelOpen'='フィルターの表示'; 'IsInlinePreviewMode'='インラインプレビュー';
    'IsTreeViewMode'='ツリー表示'; 'MaxFullImageMemoryMB'='フル画像メモリ上限（MB）';
    'NameLabelBelow'='名前を写真の下に表示'; 'OverlayPositionComboBox'='EXIFの表示位置'; 'PlatformComboBox'='SNS出力サイズ';
    'Settings.MaxMemoryCacheSizeMB'='メモリキャッシュ容量（MB）'; 'Settings.ThumbnailSize'='キャッシュ画像の大きさ';
    'ShowOriginalImages'='元画像を表示'; 'ThumbnailSize'='サムネイルの大きさ'; 'ThumbnailSlider'='サムネイルの大きさ';
    'UniformPhotoSize'='写真の表示サイズをそろえる'; 'X'='クロップの横位置'; 'Y'='クロップの縦位置'
}
foreach ($file in Get-ChildItem src/PhotoFastRater.UI/Views -Filter '*.xaml') {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    $xml.DocumentElement.SetAttribute('Foreground','{DynamicResource DarkroomPaperBrush}')
    $seen = @{}
    foreach ($node in $xml.SelectNodes('//*')) {
        $existingName = $node.GetAttribute('AutomationProperties.Name')
        if ($accessibleLabels.ContainsKey($existingName)) { $node.SetAttribute('AutomationProperties.Name',$accessibleLabels[$existingName]) }
        if ($node.LocalName -notin @('Button','ToggleButton','CheckBox','Slider','TextBox','ComboBox','DatePicker','DataGrid','TreeView','MenuItem','GridSplitter')) { continue }
        if ($node.HasAttribute('AutomationProperties.AutomationId')) { continue }
        $identity = $node.GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml')
        if (!$identity) {
            foreach ($attribute in @('Command','Click','IsChecked','Text','SelectedValue','SelectedItem','SelectedDate','Value','ItemsSource')) {
                $value = $node.GetAttribute($attribute)
                if (!$value) { continue }
                $identity = [regex]::Match($value,'Binding\s+([\w.]+)').Groups[1].Value
                if (!$identity) { $identity = $value }
                break
            }
        }
        if (!$identity) { $identity = $node.GetAttribute('Header') }
        if (!$identity) { $identity = $node.LocalName }
        $parameter = $node.GetAttribute('CommandParameter')
        if (!$parameter) { $parameter = ($node.SelectNodes("*[local-name()='Button.CommandParameter']/*") | ForEach-Object { $_.InnerText }) -join '' }
        if ($parameter -and $parameter -notmatch '^\{') { $identity += '.'+$parameter }
        $id = $file.BaseName + '.' + [regex]::Replace($identity,'[^\p{L}\p{Nd}_.-]','')
        if ($seen.ContainsKey($id)) { $seen[$id]++; $id += '.'+$seen[$id] } else { $seen[$id] = 1 }
        $node.SetAttribute('AutomationProperties.AutomationId',$id)
        if (!$node.HasAttribute('AutomationProperties.Name') -and $node.LocalName -in @('TextBox','Slider','ComboBox','DatePicker','ToggleButton','CheckBox')) {
            $label = $node.GetAttribute('Content')
            if (!$label) { $label = $node.GetAttribute('materialDesign:HintAssist.Hint') }
            if (!$label) { $label = $node.GetAttribute('ToolTip') }
            if (!$label) { $label = $identity }
            $node.SetAttribute('AutomationProperties.Name',$label)
        }
    }
    $settings = [Xml.XmlWriterSettings]::new(); $settings.Indent=$true; $settings.Encoding=[Text.UTF8Encoding]::new($false); $settings.OmitXmlDeclaration=$true
    $writer=[Xml.XmlWriter]::Create($file.FullName,$settings)
    try { $xml.Save($writer) } finally { $writer.Dispose() }
}
