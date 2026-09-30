import { initScene, startDemo, togglePause, resetDemo, toggleSpeed } from './scene.js';

// 初始化应用
window.addEventListener('DOMContentLoaded', () => {
    console.log('Neo N4 Visualization starting...');
    
    // 初始化 Three.js 场景
    initScene();
    
    // 绑定控制按钮
    setupControls();
    
    console.log('Neo N4 Visualization ready!');
});

// 设置控制按钮
function setupControls() {
    const btnStart = document.getElementById('btn-start');
    const btnPause = document.getElementById('btn-pause');
    const btnReset = document.getElementById('btn-reset');
    const btnSpeed = document.getElementById('btn-speed');
    
    let isRunning = false;
    
    // 开始按钮
    btnStart.addEventListener('click', () => {
        if (!isRunning) {
            startDemo();
            isRunning = true;
            btnStart.classList.add('active');
            btnStart.innerHTML = '▶ 运行中';
            btnPause.disabled = false;
        }
    });
    
    // 暂停按钮
    btnPause.addEventListener('click', () => {
        const isPaused = togglePause();
        if (isPaused) {
            btnPause.innerHTML = '▶ 继续';
            btnPause.classList.remove('active');
        } else {
            btnPause.innerHTML = '⏸ 暂停';
            btnPause.classList.add('active');
        }
    });
    
    // 重置按钮
    btnReset.addEventListener('click', () => {
        resetDemo();
        isRunning = false;
        btnStart.classList.remove('active');
        btnStart.innerHTML = '▶ 开始演示';
        btnPause.innerHTML = '⏸ 暂停';
        btnPause.classList.remove('active');
        btnPause.disabled = true;
        btnSpeed.innerHTML = '⚡ 2x 速度';
        btnSpeed.classList.remove('active');
    });
    
    // 速度切换按钮
    btnSpeed.addEventListener('click', () => {
        const speed = toggleSpeed();
        if (speed === 2.0) {
            btnSpeed.innerHTML = '⚡ 1x 速度';
            btnSpeed.classList.add('active');
        } else {
            btnSpeed.innerHTML = '⚡ 2x 速度';
            btnSpeed.classList.remove('active');
        }
    });
    
    // 初始状态
    btnPause.disabled = true;
}

// 导出给全局使用（可选）
window.NeoN4Viz = {
    start: startDemo,
    pause: togglePause,
    reset: resetDemo,
    toggleSpeed: toggleSpeed
};