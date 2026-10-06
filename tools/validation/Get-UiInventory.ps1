<#
.SYNOPSIS
Inventories every XAML interaction and code-only input handler without declaring it tested.
.DESCRIPTION
Stable source identities permit reviewers to compare the ledger after a redesign. Generated rows are
discovery evidence; actual execution results must be attached separately, never inferred from presence.
#>
param([string]$Output = 'docs/ja/UI_OPERATIONS.csv')
$ErrorActionPreference = 'Stop'
$rows = [Collections.Generic.List[object]]::new()
$controls = @('Button','ToggleButton','CheckBox','RadioButton','Slider','ComboBox','TextBox','DatePicker','DataGrid','MenuItem','TreeView','KeyBinding','MouseBinding','GridSplitter','TabItem')
foreach ($file in Get-ChildItem src/PhotoFastRater.UI/Views -Filter '*.xaml' | Sort-Object Name) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    $ordinal = 0
    foreach ($element in $xml.SelectNodes('//*')) {
        $handlers = @($element.Attributes | Where-Object { $_.LocalName -match '^(Click|KeyDown|PreviewKeyDown|Mouse.*|PreviewMouse.*|Drop|DragOver|SelectionChanged|SelectedItemChanged|ScrollChanged|ValueChanged|Checked|Unchecked|SizeChanged)$' })
        if ($element.LocalName -notin $controls -and !$handlers.Count) { continue }
        $ordinal++
        $binding = ($element.Attributes | Where-Object { $_.Value -like '{Binding*' } | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '; '
        $label = @('Content','Header','ToolTip','Name','Text') | ForEach-Object { $element.GetAttribute($_) } | Where-Object { $_ } | Select-Object -First 1
        $actions = ($handlers | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '; '
        $entry = if ($element.LocalName -match 'KeyBinding|MouseBinding') { ($element.Attributes | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '; ' } else { $element.LocalName }
        $step = switch ($element.LocalName) {
            'Slider' {'最小・既定・最大を設定し、値と効果を確認'}
            'TextBox' {'空・正常・境界・不正入力、入力中のキー誤発動を確認'}
            'ComboBox' {'すべての選択肢を選び、状態と結果を確認'}
            'DatePicker' {'未指定・開始/終了・逆転・境界日を確認'}
            'CheckBox' {'ON/OFFを往復し、対象と保存反映を確認'}
            'ToggleButton' {'ON/OFFを往復し、対象と表示を確認'}
            'TabItem' {'選択し、内容・フォーカス・縮小表示を確認'}
            'DataGrid' {'選択・編集可能項目・行切替・入力取消を確認'}
            'TreeView' {'展開・折り畳み・選択・キーボード移動を確認'}
            default {'入口から実行し、正常・前提不足・取消・再実行を確認'}
        }
        $rows.Add([pscustomobject]@{Id=($file.BaseName+'-'+$ordinal.ToString('D3')); View=$file.BaseName; Entry=$entry; Label=$label; Binding=$binding; Handler=$actions; Source=('src/PhotoFastRater.UI/Views/'+$file.Name); Steps=$step; Expected='UI_TEST_PLANの分野別契約とUSER_GUIDEの操作結果を満たす'; Status='NotRun'; Evidence=''; Notes=''})
    }
}
foreach ($file in Get-ChildItem src/PhotoFastRater.UI -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }) {
    foreach ($match in Select-String -LiteralPath $file.FullName -Pattern '\+=|\[RelayCommand|case Key\.|new\(\) \{ CommandName') {
        $rows.Add([pscustomobject]@{Id=($file.BaseName+'-code-'+$match.LineNumber); View=$file.BaseName; Entry='Code'; Label=$match.Line.Trim(); Binding=''; Handler=''; Source=($file.FullName.Substring($PWD.Path.Length+1).Replace('\','/')); Steps='関連画面から到達性・入口・境界・取消を確認'; Expected='分野別契約を満たし、ガイドと実動作が一致'; Status='NotRun'; Evidence=''; Notes='コード側候補。XAML入口と照合し重複/内部処理/未接続を分類する'})
    }
}
$rows | Export-Csv -LiteralPath $Output -NoTypeInformation -Encoding utf8
Write-Output "Inventoried $($rows.Count) entries into $Output; presence does not imply execution."
