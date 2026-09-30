import * as THREE from 'three';
import { gsap } from 'gsap';

// 场景配置
let scene, camera, renderer, composer;
let animationId;
let isPaused = false;
let animationSpeed = 1.0;

// 核心组件
const components = {
    l2Sequencer: null,
    neoMainchain: null,
    rollupHub: null,
    zkProver: null,
    gateway: null,
    batches: [],
    transactions: [],
    proofBeams: []
};

// 动画状态
const state = {
    currentPhase: 0,
    batchNumber: 0,
    txCount: 0,
    blockRange: { start: 0, end: 0 }
};

// 相位定义
const phases = [
    {
        name: '交易收集阶段',
        description: 'L2 序列器接收用户交易，批量打包',
        duration: 3000,
        action: 'collectTransactions'
    },
    {
        name: '批次封装阶段',
        description: '将交易打包成批次，生成状态根',
        duration: 2500,
        action: 'sealBatch'
    },
    {
        name: '零知识证明生成',
        description: 'zkVM (SP1) 生成批次执行的零知识证明',
        duration: 4000,
        action: 'generateProof'
    },
    {
        name: '批次提交到 L1',
        description: '将批次承诺和证明提交到 RollupHub',
        duration: 3000,
        action: 'submitBatch'
    },
    {
        name: '批次终局化',
        description: '验证通过，批次状态终局，更新规范根',
        duration: 2500,
        action: 'finalizeBatch'
    },
    {
        name: 'Gateway 发布',
        description: '跨链消息根发布到 SharedBridge',
        duration: 3000,
        action: 'publishToGateway'
    }
];

// 初始化场景
export function initScene() {
    const container = document.getElementById('canvas-container');
    
    // 创建场景
    scene = new THREE.Scene();
    scene.fog = new THREE.FogExp2(0x0a0e27, 0.015);
    
    // 创建相机
    camera = new THREE.PerspectiveCamera(
        60,
        window.innerWidth / window.innerHeight,
        0.1,
        1000
    );
    camera.position.set(0, 15, 35);
    camera.lookAt(0, 0, 0);
    
    // 创建渲染器
    renderer = new THREE.WebGLRenderer({ 
        antialias: true,
        alpha: true 
    });
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.setPixelRatio(window.devicePixelRatio);
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    container.appendChild(renderer.domElement);
    
    // 添加灯光
    setupLights();
    
    // 创建组件
    createComponents();
    
    // 添加粒子背景
    createParticleBackground();
    
    // 启动渲染循环
    animate();
    
    // 响应式调整
    window.addEventListener('resize', onWindowResize);
    
    // 隐藏加载界面
    document.getElementById('loading').style.display = 'none';
    
    console.log('Neo N4 visualization initialized');
}

// 设置灯光
function setupLights() {
    // 环境光
    const ambientLight = new THREE.AmbientLight(0x404060, 0.5);
    scene.add(ambientLight);
    
    // 主光源
    const mainLight = new THREE.DirectionalLight(0xffffff, 0.8);
    mainLight.position.set(10, 20, 10);
    mainLight.castShadow = true;
    mainLight.shadow.mapSize.width = 2048;
    mainLight.shadow.mapSize.height = 2048;
    scene.add(mainLight);
    
    // 点光源 - L2 序列器
    const l2Light = new THREE.PointLight(0x00ffff, 1, 50);
    l2Light.position.set(-15, 5, 0);
    scene.add(l2Light);
    
    // 点光源 - Neo 主链
    const l1Light = new THREE.PointLight(0xff6b00, 1, 50);
    l1Light.position.set(15, 5, 0);
    scene.add(l1Light);
    
    // 点光源 - RollupHub
    const hubLight = new THREE.PointLight(0x00ff88, 0.8, 40);
    hubLight.position.set(0, 2, 0);
    scene.add(hubLight);
}

// 创建组件
function createComponents() {
    // L2 序列器
    components.l2Sequencer = createL2Sequencer();
    scene.add(components.l2Sequencer);
    
    // Neo 主链
    components.neoMainchain = createNeoMainchain();
    scene.add(components.neoMainchain);
    
    // RollupHub 合约
    components.rollupHub = createRollupHub();
    scene.add(components.rollupHub);
    
    // zkVM 证明器
    components.zkProver = createZkProver();
    scene.add(components.zkProver);
    
    // Gateway 网关
    components.gateway = createGateway();
    scene.add(components.gateway);
    
    // 连接线
    createConnections();
}

// 创建 L2 序列器
function createL2Sequencer() {
    const group = new THREE.Group();
    
    // 主体 - 六边形柱体
    const geometry = new THREE.CylinderGeometry(3, 3, 6, 6);
    const material = new THREE.MeshPhongMaterial({
        color: 0x00ffff,
        emissive: 0x00ffff,
        emissiveIntensity: 0.3,
        shininess: 100,
        transparent: true,
        opacity: 0.9
    });
    const cylinder = new THREE.Mesh(geometry, material);
    cylinder.castShadow = true;
    group.add(cylinder);
    
    // 发光边缘
    const edgesGeometry = new THREE.EdgesGeometry(geometry);
    const edgesMaterial = new THREE.LineBasicMaterial({ 
        color: 0x00ffff,
        linewidth: 2
    });
    const edges = new THREE.LineSegments(edgesGeometry, edgesMaterial);
    group.add(edges);
    
    // 旋转环
    const ringGeometry = new THREE.TorusGeometry(3.5, 0.15, 16, 32);
    const ringMaterial = new THREE.MeshPhongMaterial({
        color: 0x00ffff,
        emissive: 0x00ffff,
        emissiveIntensity: 0.5
    });
    const ring = new THREE.Mesh(ringGeometry, ringMaterial);
    ring.rotation.x = Math.PI / 2;
    group.add(ring);
    
    // 动画旋转
    gsap.to(ring.rotation, {
        z: Math.PI * 2,
        duration: 4,
        repeat: -1,
        ease: 'none'
    });
    
    group.position.set(-15, 5, 0);
    group.userData.label = 'L2 Sequencer';
    
    return group;
}

// 创建 Neo 主链
function createNeoMainchain() {
    const group = new THREE.Group();
    
    // 主体 - 立方体
    const geometry = new THREE.BoxGeometry(5, 5, 5);
    const material = new THREE.MeshPhongMaterial({
        color: 0xff6b00,
        emissive: 0xff6b00,
        emissiveIntensity: 0.3,
        shininess: 100
    });
    const cube = new THREE.Mesh(geometry, material);
    cube.castShadow = true;
    group.add(cube);
    
    // 发光边缘
    const edgesGeometry = new THREE.EdgesGeometry(geometry);
    const edgesMaterial = new THREE.LineBasicMaterial({ 
        color: 0xffaa00,
        linewidth: 2
    });
    const edges = new THREE.LineSegments(edgesGeometry, edgesMaterial);
    group.add(edges);
    
    // 环绕粒子
    const particleCount = 50;
    const particles = new THREE.BufferGeometry();
    const positions = new Float32Array(particleCount * 3);
    
    for (let i = 0; i < particleCount; i++) {
        const theta = (i / particleCount) * Math.PI * 2;
        const radius = 7;
        positions[i * 3] = Math.cos(theta) * radius;
        positions[i * 3 + 1] = Math.sin(theta) * radius;
        positions[i * 3 + 2] = (Math.random() - 0.5) * 2;
    }
    
    particles.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    
    const particleMaterial = new THREE.PointsMaterial({
        color: 0xff6b00,
        size: 0.2,
        transparent: true,
        opacity: 0.8
    });
    
    const particleSystem = new THREE.Points(particles, particleMaterial);
    group.add(particleSystem);
    
    // 动画旋转
    gsap.to(cube.rotation, {
        y: Math.PI * 2,
        duration: 8,
        repeat: -1,
        ease: 'none'
    });
    
    group.position.set(15, 5, 0);
    group.userData.label = 'Neo Mainchain';
    
    return group;
}

// 创建 RollupHub 合约
function createRollupHub() {
    const group = new THREE.Group();
    
    // 主体 - 八面体
    const geometry = new THREE.OctahedronGeometry(2.5);
    const material = new THREE.MeshPhongMaterial({
        color: 0x00ff88,
        emissive: 0x00ff88,
        emissiveIntensity: 0.4,
        shininess: 100,
        transparent: true,
        opacity: 0.95
    });
    const octahedron = new THREE.Mesh(geometry, material);
    octahedron.castShadow = true;
    group.add(octahedron);
    
    // 外围框架
    const frameGeometry = new THREE.TorusGeometry(3.5, 0.1, 8, 4);
    const frameMaterial = new THREE.MeshPhongMaterial({
        color: 0x00ff88,
        emissive: 0x00ff88,
        emissiveIntensity: 0.6
    });
    
    const frame1 = new THREE.Mesh(frameGeometry, frameMaterial);
    frame1.rotation.x = Math.PI / 2;
    group.add(frame1);
    
    const frame2 = new THREE.Mesh(frameGeometry, frameMaterial);
    frame2.rotation.y = Math.PI / 2;
    group.add(frame2);
    
    // 动画旋转
    gsap.to([frame1.rotation, frame2.rotation], {
        z: Math.PI * 2,
        duration: 6,
        repeat: -1,
        ease: 'none',
        stagger: 0.5
    });
    
    gsap.to(octahedron.rotation, {
        x: Math.PI * 2,
        y: Math.PI * 2,
        duration: 10,
        repeat: -1,
        ease: 'none'
    });
    
    group.position.set(0, 2, 0);
    group.userData.label = 'RollupHub';
    
    return group;
}

// 创建 zkVM 证明器
function createZkProver() {
    const group = new THREE.Group();
    
    // 主体 - 菱形
    const geometry = new THREE.IcosahedronGeometry(2);
    const material = new THREE.MeshPhongMaterial({
        color: 0xff0088,
        emissive: 0xff0088,
        emissiveIntensity: 0.4,
        shininess: 100,
        wireframe: false
    });
    const icosahedron = new THREE.Mesh(geometry, material);
    icosahedron.castShadow = true;
    group.add(icosahedron);
    
    // 发光核心
    const coreGeometry = new THREE.SphereGeometry(0.8);
    const coreMaterial = new THREE.MeshBasicMaterial({
        color: 0xffffff,
        transparent: true,
        opacity: 0.8
    });
    const core = new THREE.Mesh(coreGeometry, coreMaterial);
    group.add(core);
    
    // 脉冲动画
    gsap.to(core.scale, {
        x: 1.3,
        y: 1.3,
        z: 1.3,
        duration: 1,
        repeat: -1,
        yoyo: true,
        ease: 'sine.inOut'
    });
    
    gsap.to(icosahedron.rotation, {
        x: Math.PI * 2,
        y: Math.PI * 2,
        z: Math.PI * 2,
        duration: 12,
        repeat: -1,
        ease: 'none'
    });
    
    group.position.set(-8, -3, -8);
    group.userData.label = 'zkVM Prover';
    
    return group;
}

// 创建 Gateway 网关
function createGateway() {
    const group = new THREE.Group();
    
    // 主体 - 圆环
    const geometry = new THREE.TorusGeometry(2, 0.5, 16, 32);
    const material = new THREE.MeshPhongMaterial({
        color: 0x8088ff,
        emissive: 0x8088ff,
        emissiveIntensity: 0.4,
        shininess: 100
    });
    const torus = new THREE.Mesh(geometry, material);
    torus.castShadow = true;
    group.add(torus);
    
    // 内部光晕
    const glowGeometry = new THREE.SphereGeometry(1.5);
    const glowMaterial = new THREE.MeshBasicMaterial({
        color: 0x8088ff,
        transparent: true,
        opacity: 0.3
    });
    const glow = new THREE.Mesh(glowGeometry, glowMaterial);
    group.add(glow);
    
    // 旋转动画
    gsap.to(torus.rotation, {
        x: Math.PI * 2,
        duration: 5,
        repeat: -1,
        ease: 'none'
    });
    
    group.position.set(8, -3, 8);
    group.userData.label = 'Gateway';
    
    return group;
}

// 创建连接线
function createConnections() {
    // L2 -> RollupHub
    createBeam(
        components.l2Sequencer.position,
        components.rollupHub.position,
        0x00ffff
    );
    
    // RollupHub -> Neo Mainchain
    createBeam(
        components.rollupHub.position,
        components.neoMainchain.position,
        0x00ff88
    );
    
    // zkProver -> RollupHub
    createBeam(
        components.zkProver.position,
        components.rollupHub.position,
        0xff0088
    );
    
    // Gateway -> RollupHub
    createBeam(
        components.gateway.position,
        components.rollupHub.position,
        0x8088ff
    );
}

// 创建光束连接
function createBeam(start, end, color) {
    const points = [];
    points.push(new THREE.Vector3(start.x, start.y, start.z));
    points.push(new THREE.Vector3(end.x, end.y, end.z));
    
    const geometry = new THREE.BufferGeometry().setFromPoints(points);
    const material = new THREE.LineBasicMaterial({
        color: color,
        transparent: true,
        opacity: 0.3,
        linewidth: 2
    });
    
    const line = new THREE.Line(geometry, material);
    scene.add(line);
}

// 创建粒子背景
function createParticleBackground() {
    const particleCount = 1000;
    const geometry = new THREE.BufferGeometry();
    const positions = new Float32Array(particleCount * 3);
    
    for (let i = 0; i < particleCount; i++) {
        positions[i * 3] = (Math.random() - 0.5) * 100;
        positions[i * 3 + 1] = (Math.random() - 0.5) * 100;
        positions[i * 3 + 2] = (Math.random() - 0.5) * 100;
    }
    
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    
    const material = new THREE.PointsMaterial({
        color: 0x8899ff,
        size: 0.1,
        transparent: true,
        opacity: 0.6
    });
    
    const particles = new THREE.Points(geometry, material);
    scene.add(particles);
}

// 动画循环
function animate() {
    animationId = requestAnimationFrame(animate);
    
    // 相机轻微摇摆
    camera.position.x = Math.sin(Date.now() * 0.0002) * 2;
    camera.lookAt(0, 0, 0);
    
    renderer.render(scene, camera);
}

// 窗口调整
function onWindowResize() {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
}

// 开始演示动画
export function startDemo() {
    console.log('Starting Neo N4 demo...');
    isPaused = false;
    runPhaseSequence();
}

// 执行相位序列
function runPhaseSequence() {
    if (isPaused || state.currentPhase >= phases.length) {
        if (state.currentPhase >= phases.length) {
            state.currentPhase = 0;
            state.batchNumber++;
        }
        return;
    }
    
    const phase = phases[state.currentPhase];
    updatePhaseUI(phase);
    
    // 执行相位动作
    executePhaseAction(phase.action);
    
    // 进入下一相位
    setTimeout(() => {
        state.currentPhase++;
        if (!isPaused) {
            runPhaseSequence();
        }
    }, phase.duration / animationSpeed);
}

// 更新相位 UI
function updatePhaseUI(phase) {
    document.getElementById('phase-title').textContent = phase.name;
    document.getElementById('phase-description').textContent = phase.description;
}

// 执行相位动作
function executePhaseAction(action) {
    switch(action) {
        case 'collectTransactions':
            collectTransactions();
            break;
        case 'sealBatch':
            sealBatch();
            break;
        case 'generateProof':
            generateProof();
            break;
        case 'submitBatch':
            submitBatch();
            break;
        case 'finalizeBatch':
            finalizeBatch();
            break;
        case 'publishToGateway':
            publishToGateway();
            break;
    }
}

// 相位动作实现
function collectTransactions() {
    state.txCount = Math.floor(Math.random() * 500) + 100;
    updateStats();
    
    // 创建交易粒子飞向 L2 序列器
    for (let i = 0; i < 20; i++) {
        setTimeout(() => {
            createTransactionParticle();
        }, i * 100);
    }
    
    document.getElementById('state-transition').textContent = '收集中...';
}

function sealBatch() {
    state.blockRange.start = state.batchNumber * 1000;
    state.blockRange.end = state.blockRange.start + 999;
    updateStats();
    
    // L2 序列器发光
    pulseComponent(components.l2Sequencer, 0x00ffff);
    
    document.getElementById('state-transition').textContent = '封装完成';
}

function generateProof() {
    // zkProver 发光并产生证明粒子
    pulseComponent(components.zkProver, 0xff0088);
    
    setTimeout(() => {
        createProofBeam(
            components.zkProver.position,
            components.rollupHub.position
        );
    }, 1500);
    
    document.getElementById('proof-status').textContent = '生成中...';
}

function submitBatch() {
    // 创建批次传输动画
    createBatchTransfer(
        components.l2Sequencer.position,
        components.rollupHub.position
    );
    
    setTimeout(() => {
        createBatchTransfer(
            components.rollupHub.position,
            components.neoMainchain.position
        );
    }, 1000);
    
    document.getElementById('proof-status').textContent = '已提交';
}

function finalizeBatch() {
    // RollupHub 和 Neo 主链同时发光
    pulseComponent(components.rollupHub, 0x00ff88);
    pulseComponent(components.neoMainchain, 0xff6b00);
    
    document.getElementById('state-transition').textContent = '已终局';
}

function publishToGateway() {
    // Gateway 发光
    pulseComponent(components.gateway, 0x8088ff);
    
    createBatchTransfer(
        components.rollupHub.position,
        components.gateway.position
    );
    
    document.getElementById('proof-status').textContent = '已发布';
}

// 创建交易粒子
function createTransactionParticle() {
    const geometry = new THREE.SphereGeometry(0.2);
    const material = new THREE.MeshBasicMaterial({
        color: 0xffaa00,
        transparent: true,
        opacity: 0.8
    });
    const particle = new THREE.Mesh(geometry, material);
    
    // 随机起始位置
    const angle = Math.random() * Math.PI * 2;
    const radius = 25;
    particle.position.set(
        Math.cos(angle) * radius,
        Math.random() * 10 - 5,
        Math.sin(angle) * radius
    );
    
    scene.add(particle);
    
    // 飞向 L2 序列器
    gsap.to(particle.position, {
        x: components.l2Sequencer.position.x,
        y: components.l2Sequencer.position.y,
        z: components.l2Sequencer.position.z,
        duration: 1.5,
        ease: 'power2.in',
        onComplete: () => {
            scene.remove(particle);
        }
    });
}

// 创建批次传输
function createBatchTransfer(start, end) {
    const geometry = new THREE.BoxGeometry(1, 1, 1);
    const material = new THREE.MeshPhongMaterial({
        color: 0xffaa00,
        emissive: 0xffaa00,
        emissiveIntensity: 0.5
    });
    const batch = new THREE.Mesh(geometry, material);
    
    batch.position.copy(start);
    scene.add(batch);
    
    gsap.to(batch.position, {
        x: end.x,
        y: end.y,
        z: end.z,
        duration: 2,
        ease: 'power2.inOut',
        onComplete: () => {
            scene.remove(batch);
        }
    });
    
    gsap.to(batch.rotation, {
        x: Math.PI * 2,
        y: Math.PI * 2,
        duration: 2,
        ease: 'none'
    });
}

// 创建证明光束
function createProofBeam(start, end) {
    const points = [];
    points.push(new THREE.Vector3(start.x, start.y, start.z));
    points.push(new THREE.Vector3(end.x, end.y, end.z));
    
    const geometry = new THREE.BufferGeometry().setFromPoints(points);
    const material = new THREE.LineBasicMaterial({
        color: 0xff0088,
        linewidth: 3
    });
    
    const beam = new THREE.Line(geometry, material);
    scene.add(beam);
    
    setTimeout(() => {
        scene.remove(beam);
    }, 2000);
}

// 组件脉冲效果
function pulseComponent(component, color) {
    gsap.to(component.scale, {
        x: 1.2,
        y: 1.2,
        z: 1.2,
        duration: 0.3,
        yoyo: true,
        repeat: 1,
        ease: 'sine.inOut'
    });
}

// 更新统计信息
function updateStats() {
    document.getElementById('batch-number').textContent = state.batchNumber;
    document.getElementById('tx-count').textContent = state.txCount;
    document.getElementById('block-range').textContent = 
        `${state.blockRange.start}-${state.blockRange.end}`;
}

// 暂停/继续
export function togglePause() {
    isPaused = !isPaused;
    if (!isPaused) {
        runPhaseSequence();
    }
    return isPaused;
}

// 重置演示
export function resetDemo() {
    isPaused = true;
    state.currentPhase = 0;
    state.batchNumber = 0;
    state.txCount = 0;
    state.blockRange = { start: 0, end: 0 };
    updateStats();
    document.getElementById('state-transition').textContent = '待处理';
    document.getElementById('proof-status').textContent = '未生成';
    document.getElementById('phase-title').textContent = '准备就绪';
    document.getElementById('phase-description').textContent = '点击开始按钮启动演示';
}

// 切换速度
export function toggleSpeed() {
    animationSpeed = animationSpeed === 1.0 ? 2.0 : 1.0;
    return animationSpeed;
}

// 清理
export function cleanup() {
    if (animationId) {
        cancelAnimationFrame(animationId);
    }
    window.removeEventListener('resize', onWindowResize);
}