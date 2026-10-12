// Extract the anchor (fragment identifier) from the current URL
let li = window.location.href.lastIndexOf("#");
var s;
if (li >= 0) {
  s = window.location.href.substring(li + 1);
}

// If an anchor was found, highlight the corresponding element
if (s != null && s.length > 0) {
  let e = document.getElementById(s);
  // If not found, try adding up to 3 trailing "-" characters, needed before md editor 2.0.451. 
  // See https://github.com/madskristensen/MarkdownEditor2022/issues/205
  if (e == null) {
    for (let i = 1; i <= 3 && e == null; i++) {
      s = s + "-";
      e = document.getElementById(s);
      // The original fragment identifier was invalid so we will not have navigated to it - do that now
      e?.scrollIntoView({behavior: "smooth", block: "start"});
    }
  }

  if (e != null) {
    let h = e.innerHTML;
    // Wrap the element's content in a <mark> tag to highlight it
    e.innerHTML = "<mark>" + h + "</mark>";
  }
}

document.addEventListener("click", evt => {
  const link = evt.target.closest('a[href^="#"]');

  if (!link) return;

  evt.preventDefault();

  let s = link.getAttribute("href").substring(1);

  console.log("Anchor:", s);

  let e = document.getElementById(s);

  // If not found, try adding up to 3 trailing "-" characters, needed before md editor 2.0.451.
  // See https://github.com/madskristensen/MarkdownEditor2022/issues/205
  if (e == null) {
    for (let i = 1; i <= 3 && e == null; i++) {
      s = s + "-";
      e = document.getElementById(s);
    }
  }
  e?.scrollIntoView({ behavior: "smooth", block: "start" });
});

// Navigate to the specified page
function gotopage(page) {
  window.location.href = page
}
