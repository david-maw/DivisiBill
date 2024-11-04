let li = window.location.href.lastIndexOf("#");
var s;
if (li >= 0) {
    s = window.location.href.substring(li + 1);
}

if (s != null && s.length > 0) {
    let e = document.getElementById(s);
    if (e != null) {
        let h = e.innerHTML;
        e.innerHTML = "<mark>" + h + "</mark>";
    }
}
function index() { gotopage("index.html"); }
function gotopage(page) {
    //alert("gotopage("+page+") called, about to switch pages")
    window.location.href = page
}
