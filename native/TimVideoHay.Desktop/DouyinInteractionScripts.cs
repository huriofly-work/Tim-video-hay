namespace TimVideoHay.Desktop;

internal static class DouyinInteractionScripts
{
    // Restrict clicks to a close control inside the recognized login dialog.
    public const string Inspect = """
    ((dismiss) => {
      const visible = e => e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
      const nodes = [...document.querySelectorAll('body div,body span,body p,body h1,body h2,body h3')].filter(visible);
      const heading = nodes.findLast(e => ['登录抖音','Log in to Douyin'].includes(e.textContent.trim()));
      const verification = nodes.some(e => ['请完成下列验证后继续','请完成验证后继续','请完成安全验证','请完成下方验证','Complete verification'].includes(e.textContent.trim()));
      // Douyin also renders the X as an unlabelled SVG. Recognize its geometry,
      // only within the top-right corner of the already identified login dialog.
      const isCross = e => {
        const svg = e.tagName.toLowerCase()==='svg' ? e : e.querySelector('svg');
        if (!svg) return false;
        const shapes=[...svg.querySelectorAll('path,line,polygon,polyline')].filter(p=>typeof p.isPointInFill==='function');
        if (!shapes.length) return false;
        try {
          const b=svg.getBBox(); if(b.width<=0||b.height<=0||b.width/b.height<.65||b.width/b.height>1.5)return false;
          const filled=(x,y)=>{const q=new DOMPoint(b.x+x*b.width,b.y+y*b.height);return shapes.some(p=>p.isPointInFill(q)||p.isPointInStroke(q));};
          return [[.23,.23],[.77,.77],[.23,.77],[.77,.23]].every(([x,y])=>filled(x,y))
            && [[.5,.18],[.5,.82],[.18,.5],[.82,.5]].every(([x,y])=>!filled(x,y));
        } catch { return false; }
      };
      let closed = false;
      if (dismiss && heading && !verification) {
        let root = heading.parentElement;
        for (let n=0; root && root !== document.body && n<8; n++,root=root.parentElement) {
          const r = root.getBoundingClientRect();
          if (r.width < 250 || r.width > 1100 || r.height < 100 || r.height > 950) continue;
          const controls = [...root.querySelectorAll('[data-e2e="login-close"],[data-e2e="douyin-login-close"],[aria-label="Close"],[aria-label="关闭"],[aria-label="关闭弹窗"],[title="关闭"],[class*="close" i],[data-e2e*="close" i],button,[role="button"],svg')];
          const close = controls.find(e => {
            if (!visible(e)) return false;
            const b=e.getBoundingClientRect(), label=(e.getAttribute('aria-label')||e.getAttribute('title')||e.textContent).trim();
            const named=['Close','关闭','关闭弹窗','×','✕','✖'].includes(label) || /close/i.test(String(e.className)+' '+(e.getAttribute('data-e2e')||''));
            return (named || b.width<=56 && b.height<=56 && isCross(e)) && b.top >= r.top && b.top < r.top+100 && b.right > r.right-110 && b.width <= 100 && b.height <= 100;
          });
          if (close) {
            if(typeof close.click==='function')close.click();
            else close.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,view:window}));
            closed=true; break;
          }
        }
      }
      return {login:!!heading,verification,closed,visibleFrames:[...document.querySelectorAll('iframe')].filter(visible).map(e=>e.name)};
    })
    """;
}
