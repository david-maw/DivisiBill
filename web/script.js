// Extract the anchor (fragment identifier) from the current URL
let li = window.location.href.lastIndexOf("#");
var s;
if (li >= 0) {
    s = window.location.href.substring(li + 1);
}

// If an anchor was found, highlight the corresponding element
if (s != null && s.length > 0) {
    let e = document.getElementById(s);
    if (e != null) {
        let h = e.innerHTML;
        // Wrap the element's content in a <mark> tag to highlight it
        e.innerHTML = "<mark>" + h + "</mark>";
    }
}

// Navigate to the specified page
function gotopage(page) {
    window.location.href = page
}
