namespace CS2ServerPortal;

internal sealed class MapReviewControl:UserControl
{
    readonly string workspace;
    readonly bool preview;
    readonly DataGridView grid=DashboardControl.Grid();
    readonly Label status=new(){Dock=DockStyle.Fill,AutoEllipsis=true};
    readonly TextBox search=new(){Width=240,PlaceholderText="Map oder Sperrgrund suchen …"};
    readonly Button refresh=DashboardControl.Button("Messwerte aktualisieren");
    readonly System.Windows.Forms.Timer timer=new(){Interval=30000};
    MapReviewSnapshot? snapshot;
    bool busy;
    public MapReviewControl(string workspace,bool preview)
    {
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        this.workspace=workspace;this.preview=preview;Dock=DockStyle.Fill;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};
        layout.RowStyles.Add(new(SizeType.Absolute,40));layout.RowStyles.Add(new(SizeType.Absolute,48));layout.RowStyles.Add(new(SizeType.Percent,100));
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};actions.Controls.AddRange([refresh,search]);
        layout.Controls.Add(actions,0,0);layout.Controls.Add(status,0,1);layout.Controls.Add(grid,0,2);Controls.Add(layout);
        foreach(var field in new[]{("name","Map",150),("id","Workshop-ID",95),("before","Bisher",65),("spawns","T / CT Spawns",70),("objectives","Bombsites / Geiseln",80),("proposal","Vorschlag",70),("block","Vorgeschlagener Ausschluss",155),("review","Prüfstand",105),("pool","Pool / manuelle Sperre",125)})
        {int index=grid.Columns.Add(field.Item1,field.Item2);grid.Columns[index].FillWeight=field.Item3;grid.Columns[index].MinimumWidth=50;}
        refresh.Click+=async(_,_)=>await RefreshAsync();search.TextChanged+=(_,_)=>Render();
        VisibleChanged+=async(_,_)=>{if(Visible&&!preview)await RefreshAsync();};
        timer.Tick+=async(_,_)=>{if(Visible&&!preview)await RefreshAsync();};if(!preview)timer.Start();
        void SizeForDpi()
        {
            float scale=DeviceDpi/96f;layout.RowStyles[0].Height=34*scale;layout.RowStyles[1].Height=44*scale;
            grid.RowTemplate.Height=grid.Font.Height+(int)(4*scale);grid.ColumnHeadersHeight=2*grid.Font.Height+(int)(6*scale);
            foreach(DataGridViewRow row in grid.Rows)row.Height=grid.RowTemplate.Height;
            refresh.AutoSize=false;refresh.Height=(int)(30*scale);refresh.Width=TextRenderer.MeasureText(refresh.Text,refresh.Font).Width+(int)(24*scale);
            search.Width=(int)(240*scale);
        }
        Load+=(_,_)=>{try{snapshot=MapReview.Read(workspace);SizeForDpi();Render();}catch(Exception e){status.Text=SafeError.Message(e);}};
        DpiChangedAfterParent+=(_,_)=>SizeForDpi();
    }
    internal async Task RefreshAsync()
    {
        if(busy||preview)return;busy=true;refresh.Enabled=false;
        try{snapshot=await MapReview.RefreshAsync(workspace);if(!IsDisposed)Render();}
        catch(Exception e){if(!IsDisposed){status.Text="Abfrage fehlgeschlagen; letzter angezeigter Stand bleibt erhalten: "+SafeError.Message(e);status.ForeColor=Color.DarkRed;}}
        finally{busy=false;if(!IsDisposed)refresh.Enabled=true;}
    }
    void Render()
    {
        grid.Rows.Clear();if(snapshot is null){status.Text="Noch keine Messwerte abgefragt. Ziel: Beispielserver TEST, 203.0.113.10:27020.";return;}
        status.ForeColor=Color.FromArgb(45,62,75);
        int pool=snapshot.LimitReview.Count(m=>m.InPool&&!m.Excluded&&m.IsMap is not false),measured=snapshot.LimitReview.Count(m=>m.InPool&&!m.Excluded&&m.IsMap is not false&&m.MeasuredAt is not null&&m.ProbeError is null);
        int assets=snapshot.LimitReview.Count(m=>m.InPool&&(m.Excluded||m.IsMap is false));
        status.Text=$"Testserver · Messstand {DashboardControl.Time(snapshot.UpdatedAt)} · {measured}/{pool} Maps vermessen + {assets} ausgenommen · {snapshot.Approved} freigegeben · {(snapshot.MeasurementOnly?"Nur Messbetrieb; Abstimmungen aus":"Abstimmungen aktiv")}\nAktualisierung bei sichtbarer Ansicht alle 30 s · Grenzen werden hier nur angezeigt; Freigabe unter !admin > Maps.";
        foreach(var m in snapshot.LimitReview.OrderByDescending(m=>m.MeasuredAt is not null).ThenBy(m=>m.Name))
        {
            if(search.Text.Length>0&&!$"{m.Id} {m.Name} {m.Result} {m.Pool}".Contains(search.Text,StringComparison.OrdinalIgnoreCase))continue;
            var p=!m.Excluded&&m.IsMap is not false&&m.MeasuredAt is not null&&m.ProbeError is null?m.LimitProposal:null;
            int index=grid.Rows.Add(m.Name,m.Id,m.Excluded||m.IsMap is false?"Entfällt":$"{m.BeforeMinPlayers??m.MinPlayers}–{m.BeforeMaxPlayers??m.MaxPlayers}",$"{m.TSpawns?.ToString()??"?"} / {m.CtSpawns?.ToString()??"?"}",$"{m.BombSites?.ToString()??"?"} / {m.Hostages?.ToString()??"?"}",m.Excluded||m.IsMap is false?"Entfällt":p is null?"Offen":$"{p.MinPlayers}–{p.MaxPlayers}",m.ProposedExclusion,m.Result,m.Pool);
            var row=grid.Rows[index];row.Cells[7].Style.BackColor=m.Approved?Color.FromArgb(225,243,231):Color.FromArgb(255,242,210);
            foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=$"{m.Name} · {m.Id}\n{m.Result}; {m.Pool}\nMessung: {DashboardControl.Time(m.MeasuredAt)}; bisherige Obergrenze: {m.ConfiguredMaxPlayers?.ToString()??"automatisch"}; Reserve: {p?.Reserve.ToString()??"offen"}\nAktuelle Map und letzte drei Maps werden zusätzlich vorübergehend aus normalen Nominierungen ausgeschlossen (Standardeinstellung).";
        }
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}


