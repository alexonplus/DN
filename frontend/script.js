const API = '';
function addEntry(name, cause, formattedDate, prepend) {
    const entriesList = document.getElementById('entriesList');
    const li = document.createElement('li');
    li.className = 'entry-item';
    li.innerHTML = `
        <div class="entry-name"></div>
        <div class="entry-details">
            <span></span>
            <span></span>
        </div>
    `;
    li.querySelector('.entry-name').textContent = name;
    li.querySelectorAll('.entry-details span')[0].textContent = `Cause: ${cause}`;
    li.querySelectorAll('.entry-details span')[1].textContent = `Time: ${formattedDate}`;
    if (prepend) {
        entriesList.insertBefore(li, entriesList.firstChild);
    } else {
        entriesList.appendChild(li);
    }
}
function formatDate(value) {
    return new Date(value).toLocaleString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}
async function loadEntries() {
    const res = await fetch(`${API}/api/tasks`);
    const { data } = await res.json();
    document.getElementById('entriesList').innerHTML = '';
    for (const entry of data) {
        addEntry(entry.name, entry.cause || 'Heart Attack', formatDate(entry.deathDate), false);
    }
}
document.getElementById('deathForm').addEventListener('submit', async function(e) {
    e.preventDefault();
    const firstNameInput = document.getElementById('firstName');
    const lastNameInput = document.getElementById('lastName');
    const causeInput = document.getElementById('cause');
    const deathDateInput = document.getElementById('deathDate');
    const firstName = firstNameInput.value.trim();
    const lastName = lastNameInput.value.trim();
    const cause = causeInput.value.trim() || 'Heart Attack';
    const res = await fetch(`${API}/api/tasks`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            name: `${firstName} ${lastName}`,
            cause: cause,
            deathDate: deathDateInput.value ? deathDateInput.value : null
        })
    });
    const saved = await res.json();
    addEntry(saved.name, saved.cause, formatDate(saved.deathDate), true);
    firstNameInput.value = '';
    lastNameInput.value = '';
    causeInput.value = '';
    deathDateInput.value = '';
    firstNameInput.focus();
});
loadEntries();