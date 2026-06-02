let socket;
let roomCode = null;
let currentQuestionId = 0;
let isOwner = false;

function connect() {
    socket = new WebSocket(`ws://${window.location.host}/ws`);

  
    socket.onmessage = (event) => {
        const msg = JSON.parse(event.data);
        switch (msg.type) {
            case "roomCreated":
                roomCode = msg.roomCode;
                isOwner = true;
                document.getElementById("roomCode").innerText = roomCode;
                document.getElementById("roomPanel").style.display = "block";
               
                
                break;
            case "joinedRoom":
                roomCode = msg.roomCode;
                document.getElementById("roomPanel").style.display = "block";
                break;
            case "playersUpdate":
                updatePlayersList(msg.players);
                break;
            case "gameStarted":
                startGameUI();
                break;
            case "newQuestion":
                displayQuestion(msg);
                break;
            case "answerResult":
                handleAnswerResult(msg);
                break;
            case "gameOver":
                showGameOver(msg.winner, msg.players);
                break;
            case "error":
                alert(msg.message);
                break;
        }
    };
    socket.onclose = () => console.log("WebSocket closed");
}

function createRoom() {
    socket.send(JSON.stringify({ type: "createRoom" }));
}

function joinRoom() {
    let code = prompt("Введите код комнаты:");
    if (code) {
        code = code.trim().toUpperCase(); 
        socket.send(JSON.stringify({ type: "joinRoom", roomCode: code }));
    }
}

function startGame() {
    if (isOwner) socket.send(JSON.stringify({ type: "startGame" }));
}

function submitAnswer(answer) {
    socket.send(JSON.stringify({ type: "answer", questionId: currentQuestionId, answer }));
}

function updatePlayersList(players) {
    const list = document.getElementById("playersList");
    list.innerHTML = "";
    players.forEach(p => {
        const li = document.createElement("li");
        li.textContent = `${p.userId} - ${p.score} очков ${p.isAlive ? "✅" : "❌"}`;
        list.appendChild(li);
    });
}

function startGameUI() {
    document.getElementById("lobbyControls").style.display = "none";
    document.getElementById("gameArea").style.display = "block";
}

function displayQuestion(q) {
    currentQuestionId = q.questionId;
    document.getElementById("questionText").innerText = q.text;
    const answersDiv = document.getElementById("answers");
    answersDiv.innerHTML = "";
    q.answers.forEach((ans, idx) => {
        const btn = document.createElement("button");
        btn.innerText = `${String.fromCharCode(65 + idx)}. ${ans}`;
        btn.onclick = () => submitAnswer(String.fromCharCode(65 + idx));
        answersDiv.appendChild(btn);
    });
    document.getElementById("questionIndex").innerText = `${q.index}/${q.total}`;
}

function handleAnswerResult(res) {
    if (res.correct) alert(`Правильно! +${res.earned} очков`);
    else alert("Неправильно! Вы выбываете.");
    if (res.eliminated) {
        
    }
}

function showGameOver(winner, players) {
    alert(`Игра окончена! Победитель: ${winner}`);
    location.reload();
}

connect();