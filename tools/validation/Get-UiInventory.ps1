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
$controls = @('Button','ToggleButton','CheckBox','RadioButton','Slider','ComboBox','TextBox','DatePicker','DataGrid','MenuItem','TreeView','KeyBinding','MouseBinding','GridSplitter','TabItem','ComboBoxItem','DataGridTextColumn','DataGridCheckBoxColumn','DataGridComboBoxColumn')
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
        $automationId = ($element.Attributes | Where-Object LocalName -eq 'AutomationProperties.AutomationId' | Select-Object -First 1).Value
        $identity = if ($automationId) { $automationId } else { $file.BaseName+'-'+$ordinal.ToString('D3') }
        $rows.Add([pscustomobject]@{Id=$identity; View=$file.BaseName; Entry=$entry; Label=$label; Binding=$binding; Handler=$actions; Source=('src/PhotoFastRater.UI/Views/'+$file.Name); Steps=$step; Expected='UI_TEST_PLANの分野別契約とUSER_GUIDEの操作結果を満たす'; Status='NotRun'; Evidence=''; Notes=''})
    }
    foreach ($action in @('Close','CloseDuringWork','Resize','MaximizeRestore','MoveMonitor','FocusOrder')) {
        $steps = switch($action) {
            'Close' {'閉じるボタン／Alt+F4／システムメニューを別入口で確認。独立画面の終了順序も確認'}
            'CloseDuringWork' {'読込・評価保存・出力中に閉じ、プロセス終了・確定済みデータ・再起動復旧を確認'}
            'Resize' {'最小サイズ、対象解像度とDPIで縮小／拡大し、決定ボタンと説明に到達できることを確認'}
            'MaximizeRestore' {'最大化と復元を往復し、写真・フォーカス・スクロール位置・ダイアログを確認'}
            'MoveMonitor' {'100↔150、150↔200%の実モニター間を移動・境界に置いて表示と位置を確認'}
            'FocusOrder' {'Tab／Shift+Tab、フォーカス表示、Enter／Space、文字入力中の写真キー抑止を確認'}
        }
        $rows.Add([pscustomobject]@{Id=($file.BaseName+'.Window.'+$action); View=$file.BaseName; Entry='Window'; Label=$action; Binding=''; Handler=''; Source=('src/PhotoFastRater.UI/Views/'+$file.Name); Steps=$steps; Expected='画面の状態・保存・操作到達性を保つ'; Status='NotRun'; Evidence=''; Notes='OSのウィンドウ入口。模擬サイズと実モニター検証を区別する'})
    }
}
foreach ($file in Get-ChildItem src/PhotoFastRater.UI -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }) {
    $sourceLines = @(Get-Content -LiteralPath $file.FullName)
    foreach ($match in Select-String -LiteralPath $file.FullName -Pattern '\+=|\[RelayCommand|Key\.\w+|new\(\) \{ CommandName') {
        $symbol = ''
        if ($match.Line -match '\[RelayCommand') {
            $following = $sourceLines[$match.LineNumber..([Math]::Min($sourceLines.Count-1,$match.LineNumber+8))] -join "`n"
            $symbol = [regex]::Match($following,'\b(?:private|public)\s+(?:async\s+)?[^\r\n(]+?\s+(\w+)\s*\(').Groups[1].Value
        }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($match.Line.Trim()))).Substring(0,12)
        $preceding = $sourceLines[0..($match.LineNumber-1)] -join "`n"
        $methods = [regex]::Matches($preceding,'\b(?:private|public|internal|protected)\s+(?:async\s+|static\s+|partial\s+)*[\w.]+(?:<[^;\r\n=()]+>)?\??\s+(\w+)(?:<[^>]+>)?\s*\(')
        $scope = if($methods.Count) { $methods[$methods.Count-1].Groups[1].Value } else { 'Initialization' }
        $identity = if($symbol) { $file.BaseName+'.Command.'+$symbol } else { $file.BaseName+'.Code.'+$scope+'.'+$hash }
        $command = if($symbol) { $symbol -replace 'Async$','' } else { '' }
        $rows.Add([pscustomobject]@{Id=$identity; View=$file.BaseName; Entry='Code'; Label=$match.Line.Trim(); Binding=$command; Handler=''; Source=($file.FullName.Substring($PWD.Path.Length+1).Replace('\','/')); Steps='関連画面から到達性・入口・境界・取消を確認'; Expected='分野別契約を満たし、ガイドと実動作が一致'; Status='NotRun'; Evidence=''; Notes='コード側候補。XAML入口と照合し重複/内部処理/未接続を分類する'})
    }
}
$contracts = @(
    @{Match='SetRating|ToggleFavorite|ToggleReject|Rating_Click'; Purpose='選択写真の評価・フラグを変更する'; Preconditions='コピー写真を1枚選択。保存失敗用の専用fixtureも用意'; Inputs='星0～5、ON/OFF、連打、再起動'; Expected='対象とペアの保存値・表示値が一致。失敗時に成功表示しない'; Effects='DB／XMPまたはフォルダセッション。明示埋込以外は原本画像を変更しない'; Guide='USER_GUIDE.md#1枚を選ぶことと一括対象のチェック'},
    @{Match='Export|Crop|Frame|Overlay|Platform|Rotation|Quality'; Purpose='選択／チェック写真を設定した形式で出力する'; Preconditions='コピー画像と専用出力先。1枚選択とバッチチェックを区別'; Inputs='全選択肢、数値境界、空／不正値、同名、欠損、取消'; Expected='出力設定・寸法・向きと結果が一致。失敗分を特定して再試行できる'; Effects='専用出力先へ新規ファイル。原本と既存出力を保持'; Guide='USER_GUIDE.md#出力'},
    @{Match='Collection|Organization|Tag|Event'; Purpose='写真をタグ・階層コレクション・イベントで整理する'; Preconditions='一括対象をチェック。必要に応じ親項目または1枚を選択'; Inputs='空／日本語／重複、未選択、候補確認・確定・再確定'; Expected='対象と所属が一致し、再実行で重複しない。候補だけでは確定しない'; Effects='DBの関連付けを更新。画像ファイルを移動しない'; Guide='USER_GUIDE.md#タグコレクションイベント'},
    @{Match='Settings|Shortcuts|ManagedFolders|Exclusion'; Purpose='動作設定・キー・管理対象を変更する'; Preconditions='専用プロファイル。設定前の値と保存ファイルを記録'; Inputs='保存／取消／初期化、数値境界、重複キー、不正JSON・Regex・書込拒否'; Expected='保存と取消の結果を区別し、再起動で値を保持。失敗時は元データを保持'; Effects='専用設定JSON／キーJSON／管理DB。キャッシュ消去は確認後'; Guide='USER_GUIDE.md#設定と管理フォルダ'},
    @{Match='Compare|Zoom|Pin|Swap|Advance|Pane'; Purpose='複数写真を比較して候補を選ぶ'; Preconditions='2／3／4枚と不足枚数。現在保持範囲に写真を用意'; Inputs='同期ON/OFF、Ctrl+ホイール、ドラッグ、固定、入替、次の組、終端'; Expected='対象・位置・倍率・固定が一致。同期OFFでは他ペインを変えない'; Effects='表示状態と明示評価だけを変更'; Guide='USER_GUIDE.md#閲覧と比較'},
    @{Match='FolderMode|PhotoPreview'; Purpose='フォルダセッションを選別・閲覧する'; Preconditions='コピーの空／1～4枚／大量／RAWペアを用意'; Inputs='全フィルター、日付逆転、再読込、保存、先頭／終端、表示切替'; Expected='表示件数・選択・保存値・プレビューが一致。エラーから復帰できる'; Effects='専用セッション。DB追加・JPEG埋込は別の明示確認'; Guide='USER_GUIDE.md#フォルダモード'},
    @{Match='.*'; Purpose='ライブラリを起動・表示・操作する'; Preconditions='空／登録済みの専用カタログ。コピー素材と失敗fixture'; Inputs='正常／未選択／取消／境界／再実行／終了順序'; Expected='表示・対象・状態・保存結果がガイドと一致し、入力を奪わない'; Effects='読み込み、専用DB・ログ・キャッシュ。破壊的操作は確認とコピーのみ'; Guide='USER_GUIDE.md'}
)
$ledger = foreach ($row in $rows) {
    $identity = $row.Id+' '+$row.Binding
    $contract = $contracts | Where-Object { $identity -match $_.Match } | Select-Object -First 1
    $row.Expected = $contract.Expected
    $row | Select-Object *, @{Name='Purpose';Expression={$contract.Purpose}},
        @{Name='Preconditions';Expression={$contract.Preconditions}}, @{Name='InputsAndErrors';Expression={$contract.Inputs}},
        @{Name='PersistenceAndEffects';Expression={$contract.Effects}}, @{Name='Guide';Expression={$contract.Guide}}
}
$ledger | Export-Csv -LiteralPath $Output -NoTypeInformation -Encoding utf8
Write-Output "Inventoried $($rows.Count) entries into $Output; presence does not imply execution."
