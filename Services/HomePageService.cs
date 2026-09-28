using System.Linq;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Horizon.Stealth.Services;

public static class HomePageService
{
    public static string GenerateHtml()
    {
        string searchUrl = SettingsService.Current.SearchEngineUrl;
        if (string.IsNullOrWhiteSpace(searchUrl))
            searchUrl = "https://alohafind.com/search/?q={query}";

        // Split on {query} so we can emit: prefix + encodeURIComponent(q) + suffix
        var parts = searchUrl.Split(new[] { "{query}" }, 2, StringSplitOptions.None);
        string jsPre  = parts[0];
        string jsPost = parts.Length > 1 ? parts[1] : "";

        // ── Google Account Switcher ───────────────────────────────────────────
        // Use GoogleBrowserAccounts (populated from the browser's signed-in sessions)
        // so the button shows even when no OAuth SyncAccounts have been set up.
        var browserAccts = SettingsService.Current.GoogleBrowserAccounts;
        var acctOrder = SettingsService.Current.GoogleAccountOrder;
        if (acctOrder.Count > 0)
            browserAccts = browserAccts
                .OrderBy(a => { var idx = acctOrder.IndexOf(a.Email); return idx < 0 ? 999 : idx; })
                .ToList();
        string switcherHtml = "";
        if (browserAccts.Count > 0)
        {
            string acctDef = SettingsService.Current.DefaultGoogleAccountEmail;
            if (string.IsNullOrEmpty(acctDef)) acctDef = browserAccts[0].Email;
            string acctJson = JsonSerializer.Serialize(
                browserAccts.Select(a => new { email = a.Email, name = a.Name, avatar = a.AvatarUrl }));
            double? bx = SettingsService.Current.AccountSwitcherButtonX;
            double? by = SettingsService.Current.AccountSwitcherButtonY;
            switcherHtml = BuildSwitcherScript(
                acctJson, acctDef, "false",
                bx.HasValue ? bx.Value.ToString("F0") : "null",
                by.HasValue ? by.Value.ToString("F0") : "null");
        }

        var html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <style>
                body {{ background-color: #000; color: #FFF; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100vh; margin: 0; overflow: hidden; user-select: none; }}
                .time {{ font-size: 92px; font-weight: 100; opacity: 0.9; letter-spacing: -2px; text-shadow: 0 0 20px rgba(255,255,255,0.1); }}
                .date {{ font-size: 18px; color: #666; font-weight: 500; text-transform: uppercase; letter-spacing: 2px; margin-bottom: 50px; }}
                .search-container {{ position: relative; width: 600px; }}
                .search-pill {{ background: linear-gradient(180deg, #1A1A1A 0%, #0F0F0F 100%); width: 100%; height: 56px; border-radius: 28px; border: 1px solid #333; display: flex; align-items: center; padding: 0 20px; box-shadow: 0 10px 30px rgba(0,0,0,0.5); transition: all 0.2s ease; }}
                .search-pill:focus-within {{ border-color: #666; box-shadow: 0 0 0 4px rgba(255,255,255,0.05); transform: scale(1.02); }}
                input {{ background: transparent; border: none; outline: none; color: #FFF; font-size: 18px; width: 100%; margin-left: 10px; font-weight: 300; }}
                ::placeholder {{ color: #444; }}
                .shortcuts {{ display: flex; gap: 20px; margin-top: 40px; }}
                .icon {{ width: 60px; height: 60px; background: #111; border-radius: 18px; display: flex; align-items: center; justify-content: center; font-size: 24px; cursor: pointer; transition: all 0.2s cubic-bezier(0.25, 0.8, 0.25, 1); border: 1px solid #222; }}
                .icon:hover {{ background: #222; transform: translateY(-5px); border-color: #444; box-shadow: 0 5px 15px rgba(0,0,0,0.3); }}
                .icon:active {{ transform: scale(0.95); }}
            </style>
            <script>
                function tick() {{
                    const d = new Date();
                    document.getElementById('t').innerText = d.toLocaleTimeString([], {{hour:'2-digit', minute:'2-digit', hour12: false}});
                    document.getElementById('d').innerText = d.toLocaleDateString([], {{weekday:'long', month:'long', day:'numeric'}});
                }}
                function s(e) {{ if(e.key === 'Enter') window.location.href = '{jsPre}' + encodeURIComponent(document.getElementById('q').value) + '{jsPost}'; }}
                setInterval(tick, 1000);
            </script>
        </head>
        <body onload='tick()'>
            <div class='time' id='t'>00:00</div>
            <div class='date' id='d'>Gathering Data...</div>
            <div class='search-container'>
                <div class='search-pill'>
                    <span style='opacity:0.5'>🔍</span>
                    <input id='q' type='text' placeholder='Search or type URL...' onkeydown='s(event)' autofocus>
                </div>
            </div>
            <div class='shortcuts'>
                <div class='icon' onclick=""location.href='https://youtube.com'"" title='YouTube'>📺</div>
                <div class='icon' onclick=""location.href='https://github.com'"" title='GitHub'>🐙</div>
                <div class='icon' onclick=""location.href='https://reddit.com'"" title='Reddit'>👽</div>
                <div class='icon' onclick=""location.href='https://chatgpt.com'"" title='AI'>🤖</div>
                <div class='icon' onclick=""location.href='https://ghappstore-j93n3sq2.manus.space/'"" title='AppStore'>🧩</div>
            </div>
            {switcherHtml}
        </body>
        </html>";

        return "data:text/html;charset=utf-8," + HttpUtility.UrlEncode(html);
    }

    public static string BuildSwitcherScript(
        string accountsJson, string defaultEmail, string isGoogle, string savedX, string savedY)
    {
        return @"<script>
(function() {
    if (document.getElementById('hz-acct-btn')) return;
    var ACCTS = __ACCOUNTS__;
    var DEF = '__DEFAULT__';
    var IS_GOOGLE = __IS_GOOGLE__;
    var SX = __SAVED_X__;
    var SY = __SAVED_Y__;
    if (!ACCTS || !ACCTS.length) return;
    var accts = ACCTS.slice();
    var def = DEF || (accts[0] ? accts[0].email : '');
    function ini(a) { return ((a.name||a.email||'?')[0]||'?').toUpperCase(); }
    function mkAv(cls,a) {
        var d=document.createElement('div'); d.className=cls;
        if (a.avatar) {
            var img=document.createElement('img'); img.src=a.avatar;
            img.onerror=function(){d.innerHTML='';d.textContent=ini(a);};
            d.appendChild(img);
        } else { d.textContent=ini(a); }
        return d;
    }
    function post(o){try{window.chrome.webview.postMessage(JSON.stringify(o));}catch(e){}}
    var s=document.createElement('style');
    s.textContent=
        '#hz-acct-btn{position:fixed;width:44px;height:44px;border-radius:50%;'
        +'background:rgba(20,20,20,.92);border:2px solid rgba(255,255,255,.12);cursor:pointer;'
        +'display:flex;align-items:center;justify-content:center;z-index:2147483640;'
        +'box-shadow:0 4px 14px rgba(0,0,0,.45);transition:border-color .2s,transform .15s;user-select:none;}'
        +'#hz-acct-btn:hover{border-color:rgba(255,255,255,.3);transform:scale(1.08);}'
        +'#hz-acct-btn.hz-mv{border-color:#4af!important;cursor:crosshair;animation:hz-p 1.2s ease-in-out infinite;}'
        +'@keyframes hz-p{0%,100%{box-shadow:0 4px 14px rgba(0,0,0,.45);}50%{box-shadow:0 0 0 6px rgba(68,170,255,.18),0 4px 14px rgba(0,0,0,.45);}}'
        +'.hz-av{width:32px;height:32px;border-radius:50%;background:#2a2a2a;display:flex;align-items:center;'
        +'justify-content:center;font-size:14px;font-weight:700;color:#fff;overflow:hidden;flex-shrink:0;pointer-events:none;}'
        +'.hz-av img,.hz-avs img{width:100%;height:100%;object-fit:cover;}'
        +'#hz-popup{position:fixed;background:rgba(18,18,18,.97);border:1px solid rgba(255,255,255,.1);'
        +'border-radius:14px;padding:6px;min-width:252px;z-index:2147483641;box-shadow:0 12px 40px rgba(0,0,0,.65);display:none;}'
        +'.hz-ptitle{font-size:10px;color:rgba(255,255,255,.3);text-transform:uppercase;letter-spacing:1.2px;padding:6px 12px 8px;}'
        +'.hz-row{display:flex;align-items:center;gap:10px;padding:7px 10px;border-radius:9px;cursor:pointer;transition:background .12s;}'
        +'.hz-row:hover{background:rgba(255,255,255,.07);}'
        +'.hz-row.hz-sel{background:rgba(68,170,255,.1);}'
        +'.hz-row.hz-drag{opacity:.4;}'
        +'.hz-avs{width:30px;height:30px;border-radius:50%;background:#333;display:flex;align-items:center;'
        +'justify-content:center;font-size:13px;font-weight:700;color:#fff;overflow:hidden;flex-shrink:0;}'
        +'.hz-info{flex:1;min-width:0;}'
        +'.hz-nm{font-size:13px;color:rgba(255,255,255,.9);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}'
        +'.hz-em{font-size:11px;color:rgba(255,255,255,.38);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}'
        +'.hz-ck{font-size:13px;color:#4af;flex-shrink:0;}'
        +'.hz-hdl{cursor:ns-resize;color:rgba(255,255,255,.2);font-size:14px;flex-shrink:0;padding:0 2px;transition:color .15s;line-height:1;}'
        +'.hz-hdl:hover{color:rgba(255,255,255,.55);}'
        +'#hz-ctx{position:fixed;background:rgba(22,22,22,.97);border:1px solid rgba(255,255,255,.1);'
        +'border-radius:10px;padding:4px;z-index:2147483642;box-shadow:0 6px 24px rgba(0,0,0,.55);display:none;min-width:160px;}'
        +'.hz-ci{padding:8px 14px;font-size:13px;color:rgba(255,255,255,.8);border-radius:7px;cursor:pointer;white-space:nowrap;transition:background .1s;}'
        +'.hz-ci:hover{background:rgba(255,255,255,.08);color:#fff;}'
        +'#hz-toast{position:fixed;bottom:76px;left:50%;transform:translateX(-50%);'
        +'background:rgba(20,20,20,.92);border:1px solid rgba(255,255,255,.15);color:rgba(255,255,255,.65);'
        +'padding:7px 18px;border-radius:20px;font-size:12px;z-index:2147483643;display:none;pointer-events:none;white-space:nowrap;}';
    document.head.appendChild(s);
    var btn=document.createElement('div'); btn.id='hz-acct-btn'; btn.title='Switch Google account';
    function rfAv(){
        btn.innerHTML='';
        var a=accts.filter(function(x){return x.email===def;})[0]||accts[0];
        if(a) btn.appendChild(mkAv('hz-av',a));
    }
    function setPos(){
        if(SX!==null&&SY!==null){btn.style.cssText='left:'+SX+'px;top:'+SY+'px;right:auto;bottom:auto;';}
        else{btn.style.cssText='right:20px;bottom:20px;left:auto;top:auto;';}
    }
    rfAv(); setPos(); document.body.appendChild(btn);
    var popup=document.createElement('div'); popup.id='hz-popup'; document.body.appendChild(popup);
    var ctx=document.createElement('div'); ctx.id='hz-ctx';
    var ci=document.createElement('div'); ci.className='hz-ci'; ci.id='hz-ci-mv';
    ci.textContent='\u2725\u2002Move button'; ctx.appendChild(ci); document.body.appendChild(ctx);
    var toast=document.createElement('div'); toast.id='hz-toast';
    toast.textContent='Click to place  \u00b7  Esc to cancel'; document.body.appendChild(toast);
    function closeAll(){popup.style.display='none';ctx.style.display='none';}
    var dragSrc=null;
    function buildPopup(){
        popup.innerHTML='';
        var title=document.createElement('div'); title.className='hz-ptitle'; title.textContent='Switch Google account';
        popup.appendChild(title);
        accts.forEach(function(a){
            var row=document.createElement('div');
            row.className='hz-row'+(a.email===def?' hz-sel':'');
            row.dataset.email=a.email;
            var hdl=document.createElement('div'); hdl.className='hz-hdl'; hdl.textContent='\u283f'; hdl.title='Drag to reorder';
            var av=mkAv('hz-avs',a);
            var info=document.createElement('div'); info.className='hz-info';
            var nm=document.createElement('div'); nm.className='hz-nm'; nm.textContent=a.name||a.email;
            var em=document.createElement('div'); em.className='hz-em'; em.textContent=a.email;
            info.appendChild(nm); info.appendChild(em);
            var ck=document.createElement('div'); ck.className='hz-ck'; ck.textContent=(a.email===def?'\u2713':'');
            row.appendChild(hdl); row.appendChild(av); row.appendChild(info); row.appendChild(ck);
            popup.appendChild(row);
            row.addEventListener('click',function(e){
                if(hdl.contains(e.target))return;
                def=a.email;
                post({type:'google_account_switch',email:a.email,order:accts.map(function(x){return x.email;})});
                rfAv(); closeAll();
            });
            hdl.addEventListener('mousedown',function(e){dragSrc=row;row.classList.add('hz-drag');e.preventDefault();});
        });
        popup.addEventListener('mouseover',function(e){
            if(!dragSrc)return;
            var t=e.target&&e.target.closest?e.target.closest('.hz-row'):null;
            if(t&&t!==dragSrc){
                var rs=[].slice.call(popup.querySelectorAll('.hz-row'));
                if(rs.indexOf(dragSrc)<rs.indexOf(t))t.after(dragSrc);else t.before(dragSrc);
            }
        });
    }
    document.addEventListener('mouseup',function(){
        if(!dragSrc)return;
        dragSrc.classList.remove('hz-drag');
        var ord=[].slice.call(popup.querySelectorAll('.hz-row')).map(function(r){return r.dataset.email;});
        accts=ord.map(function(e){return accts.filter(function(a){return a.email===e;})[0];}).filter(Boolean);
        post({type:'google_account_order',order:ord});
        dragSrc=null;
    });
    function posPopup(){
        var br=btn.getBoundingClientRect(),pw=252,ph=accts.length*52+40;
        var x=br.right-pw,y=br.top-ph-8;
        if(x<8)x=8; if(y<8)y=br.bottom+8;
        if(x+pw>window.innerWidth-8)x=window.innerWidth-pw-8;
        popup.style.left=x+'px'; popup.style.top=y+'px'; popup.style.display='block';
    }
    btn.addEventListener('click',function(e){
        if(btn.classList.contains('hz-mv'))return;
        e.stopPropagation();
        if(popup.style.display==='block'){closeAll();return;}
        buildPopup(); posPopup();
    });
    btn.addEventListener('contextmenu',function(e){
        e.preventDefault(); e.stopPropagation(); closeAll();
        ctx.style.display='block'; ctx.style.left=e.clientX+'px'; ctx.style.top=e.clientY+'px';
        var r=ctx.getBoundingClientRect();
        if(r.right>window.innerWidth-8)ctx.style.left=(window.innerWidth-r.width-8)+'px';
        if(r.bottom>window.innerHeight-8)ctx.style.top=(window.innerHeight-r.height-8)+'px';
    });
    document.getElementById('hz-ci-mv').addEventListener('click',function(){closeAll();enterMv();});
    document.addEventListener('click',function(e){
        if(!btn.contains(e.target)&&!popup.contains(e.target))popup.style.display='none';
        if(!ctx.contains(e.target))ctx.style.display='none';
    },true);
    var inMv=false,savedCSS='';
    function enterMv(){
        inMv=true; savedCSS=btn.style.cssText;
        btn.classList.add('hz-mv'); toast.style.display='block';
        document.addEventListener('mousemove',onMv);
        document.addEventListener('keydown',onEsc);
        document.addEventListener('click',onPlace,{capture:true});
    }
    function exitMv(save){
        inMv=false; btn.classList.remove('hz-mv'); toast.style.display='none';
        document.removeEventListener('mousemove',onMv);
        document.removeEventListener('keydown',onEsc);
        document.removeEventListener('click',onPlace,{capture:true});
        if(save)post({type:'account_switcher_move',x:Math.round(parseFloat(btn.style.left)),y:Math.round(parseFloat(btn.style.top))});
    }
    function onMv(e){btn.style.cssText='left:'+Math.max(0,e.clientX-22)+'px;top:'+Math.max(0,e.clientY-22)+'px;right:auto;bottom:auto;';}
    function onEsc(e){if(e.key==='Escape'){exitMv(false);btn.style.cssText=savedCSS;}}
    function onPlace(e){e.stopPropagation();e.preventDefault();exitMv(true);}
})();
</script>"
            .Replace("__ACCOUNTS__", accountsJson)
            .Replace("__DEFAULT__", defaultEmail.Replace("'", ""))
            .Replace("__IS_GOOGLE__", isGoogle)
            .Replace("__SAVED_X__", savedX)
            .Replace("__SAVED_Y__", savedY);
    }
}