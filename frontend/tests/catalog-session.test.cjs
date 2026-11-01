const {test}=require('node:test'), assert=require('node:assert/strict'), fs=require('node:fs'), vm=require('node:vm'), path=require('node:path');
const context=vm.createContext({window:{}});
vm.runInContext(fs.readFileSync(path.join(__dirname,'../js/common/catalog-session.js'),'utf8'),context);
const {CatalogSession,ingredientSnapshot,recipeSnapshot,pendingKey}=context.window.restaurantCatalog;
const copy=x=>JSON.parse(JSON.stringify(x));
function storage(){const map=new Map();return {getItem:k=>map.get(k)||null,setItem:(k,v)=>map.set(k,v),removeItem:k=>map.delete(k)};}
function setup(role='WarehouseManager',saved=storage()) {
    const calls=[],auth={role,userId:9,hasRole:(...roles)=>roles.includes(auth.role),getUser:()=>({userId:auth.userId})};
    const ingredients=[{ingredientId:1,name:'Gạo',unit:'kg',minimumStock:1,isActive:true,stockStatus:'Available'}, {ingredientId:2,name:'Dầu',unit:'lít',minimumStock:0,isActive:false,stockStatus:'Spoiled'}, {ingredientId:3,name:'Muối mới',unit:'g',minimumStock:0,isActive:true,stockStatus:'Available'}];
    const recipes=[{recipeId:31,sizeId:11,name:'Cơm đặc biệt',version:'2',isActive:true,ingredientCount:1},{recipeId:30,sizeId:11,name:'Cơm cũ',version:'1',isActive:false,ingredientCount:1}];
    const details={31:{...recipes[0],foodName:'Cơm',sizeName:'Phần đặc biệt',ingredients:[{ingredientId:1,ingredientName:'Gạo',quantity:0.2,unit:'kg'}]},30:{...recipes[1],foodName:'Cơm',sizeName:'Phần đặc biệt',ingredients:[{ingredientId:1,ingredientName:'Gạo',quantity:0.1,unit:'kg'}]}};
    const server={ingredients,recipes,details,foods:[{foodId:1,foodName:'Cơm',isActive:true}],sizes:[{sizeId:11,sizeName:'Phần đặc biệt',isActive:true},{sizeId:12,sizeName:'Hộp',isActive:true},{sizeId:13,sizeName:'Cũ',isActive:false}],inventory:[{ingredientId:1,quanlity:10}],ledger:0,purchases:[],purchaseDetails:{},get:null,write:null};
    const api={get:async url=>{
        calls.push({method:'get',url});if(server.get)return server.get(url);
        if(url==='/management/ingredients')return copy(server.ingredients);
        if(url==='/management/recipes?includeInactive=true')return copy(server.recipes);
        if(url==='/foods?includeInactive=true')return copy(server.foods);
        if(url==='/management/inventory')return copy(server.inventory);
        if(url.startsWith('/foods/'))return copy(server.sizes);
        if(url.startsWith('/management/inventory-transactions?'))return {items:[],total:server.ledger};
        if(url==='/management/purchase-orders')return copy(server.purchases);
        if(url.startsWith('/management/purchase-orders/'))return copy(server.purchaseDetails[Number(url.split('/').at(-1))]);
        if(url.startsWith('/management/ingredients/'))return copy(server.ingredients.find(i=>i.ingredientId===Number(url.split('/').at(-1))));
        if(url.startsWith('/management/recipes/'))return copy(server.details[Number(url.split('/').at(-1))]);
        throw new Error('Unexpected GET '+url);
    }};
    for(const method of ['post','put','patch','delete'])api[method]=async(url,body)=>{calls.push({method,url,body:copy(body??null)});return server.write?server.write(method,url,body):{ingredientId:4,recipeId:40};};
    return {session:new CatalogSession({api,auth,storage:saved}),api,auth,server,calls,saved};
}
const writes=b=>b.calls.filter(c=>c.method!=='get');
const recipeData={sizeId:12,name:'Công thức hộp',version:'',ingredients:[{ingredientId:1,quantity:0.125}]};
test('Warehouse/Admin đọc cả nguyên liệu inactive, lịch sử và size ngoài Nhỏ/Vừa/Lớn',async()=>{
    for(const role of ['WarehouseManager','Admin']){const b=setup(role);await b.session.refresh();assert.equal(b.session.ingredients.length,3);assert.equal(b.session.recipes.length,2);assert.equal(b.session.sizes[0].sizeName,'Phần đặc biệt');assert.equal(b.session.sizes[2].isActive,false);assert.ok(b.calls.some(c=>c.url==='/foods/1/sizes?includeInactive=true'));}
});
test('Waiter/Chef/Cashier không đọc hoặc ghi danh mục, không gửi HTTP',async()=>{
    for(const role of ['Waiter','Chef','Cashier']){const b=setup(role);await assert.rejects(b.session.refresh(),e=>e.status===403);await assert.rejects(b.session.command('createIngredient'),e=>e.status===403);assert.equal(b.calls.length,0);}
});
test('Tạo nguyên liệu trim tên/đơn vị, min=0 hợp lệ; không gửi số tồn/actor giả',async()=>{
    const b=setup();await b.session.refresh();await b.session.command('createIngredient',null,{name:' Muối ',unit:' g ',minimumStock:'0'});
    assert.deepEqual(writes(b)[0],{method:'post',url:'/management/ingredients',body:{name:'Muối',unit:'g',minimumStock:0}});assert.equal(b.session.pending,null);
});
test('Thiếu tên/đơn vị, quá dài, số âm, 4 số lẻ và ô trắng không tạo nguyên liệu',async()=>{
    for(const data of [{name:' '},{unit:' '},{name:'x'.repeat(151)},{unit:'x'.repeat(51)},{minimumStock:-1},{minimumStock:0.0001},{minimumStock:' '},{minimumStock:Infinity}]){
        const b=setup();await b.session.refresh();await assert.rejects(b.session.command('createIngredient',null,{name:'Gạo',unit:'kg',minimumStock:1,...data}),e=>e.status===400);assert.equal(writes(b).length,0);
    }
});
test('Nguyên liệu chưa dùng sửa được đơn vị sau khi kiểm tra đủ ledger/công thức/phiếu nhập',async()=>{
    const b=setup();await b.session.refresh();await b.session.command('updateIngredient',3,{name:'Muối',unit:'kg',minimumStock:0},ingredientSnapshot(b.server.ingredients[2]));
    assert.equal(writes(b)[0].method,'put');assert.equal(writes(b)[0].body.unit,'kg');assert.ok(b.calls.some(c=>c.url==='/management/purchase-orders'));
});
test('Đã có ledger: đổi đơn vị bị chặn trước PUT',async()=>{
    const b=setup();b.server.ledger=1;await b.session.refresh();await assert.rejects(b.session.command('updateIngredient',1,{name:'Gạo',unit:'g',minimumStock:1}),/đơn vị/);assert.equal(writes(b).length,0);
});
test('Đơn vị bị khóa bởi công thức cũ ngay cả khi không còn bản active',async()=>{
    const b=setup();b.server.recipes=b.server.recipes.filter(r=>!r.isActive);await b.session.refresh();const usage=await b.session.ingredientUsage(1);assert.equal(usage.locked,true);assert.match(usage.reason,/phiên bản cũ/);
});
test('Đơn vị bị khóa bởi phiếu nháp/hủy chưa có ledger',async()=>{
    const b=setup();b.server.purchases=[{id:4,status:'Cancelled'}];b.server.purchaseDetails[4]={items:[{ingredientId:3,quantity:1}]};await b.session.refresh();assert.equal((await b.session.ingredientUsage(3)).locked,true);
});
test('Sửa tên/ngưỡng giữ đơn vị cũ, không bắt đọc toàn bộ lịch sử',async()=>{
    const b=setup();await b.session.refresh();await b.session.command('updateIngredient',1,{name:'Gạo mới',unit:'KG',minimumStock:2});assert.equal(writes(b)[0].body.minimumStock,2);assert.equal(b.calls.some(c=>c.url.startsWith('/management/inventory-transactions?')),false);
});
test('Lookup sử dụng đơn vị lỗi thì chặn đổi đơn vị, không gửi lệnh hoặc lưu pending giả',async()=>{
    const b=setup();await b.session.refresh();const original=b.api.get;b.api.get=url=>url.startsWith('/management/inventory-transactions?')?Promise.reject(Object.assign(new Error('Lỗi đọc ledger'),{status:500})):original(url);
    await assert.rejects(b.session.command('updateIngredient',3,{name:'Muối',unit:'kg',minimumStock:0}),/Lỗi đọc ledger/);assert.equal(writes(b).length,0);assert.equal(b.session.pending,null);
});
test('Snapshot nguyên liệu đổi trong lúc mở form thì không ghi đè',async()=>{
    const b=setup();await b.session.refresh();const expected=ingredientSnapshot(b.server.ingredients[0]);b.server.ingredients[0].minimumStock=8;
    await assert.rejects(b.session.command('updateIngredient',1,{name:'Gạo',unit:'kg',minimumStock:2},expected),/vừa thay đổi/);assert.equal(writes(b).length,0);
});
test('Ngừng/khôi phục/tình trạng gọi đúng route và giữ lượng tồn',async()=>{
    const b=setup();await b.session.refresh();await b.session.command('deleteIngredient',1);await b.session.command('ingredientActive',1,{isActive:true});await b.session.command('ingredientStatus',1,{stockStatus:'Spoiled'});
    assert.deepEqual(writes(b).map(c=>[c.method,c.url,c.body]),[['delete','/management/ingredients/1',null],['patch','/management/ingredients/1/active',{isActive:true}],['patch','/management/ingredients/1/status',{stockStatus:'Spoiled'}]]);assert.equal(b.session.inventory[0].quanlity,10);
});
test('Không nhận trạng thái tự đặt hoặc isActive kiểu chuỗi',async()=>{
    for(const [kind,data] of [['ingredientStatus',{stockStatus:'Ready'}],['ingredientActive',{isActive:'false'}]]){const b=setup();await b.session.refresh();await assert.rejects(b.session.command(kind,1,data),e=>e.status===400);assert.equal(writes(b).length,0);}
});
test('Tạo công thức giữ quantity JSON, phiên bản trống cho server, không giới hạn tên size',async()=>{
    const b=setup();await b.session.refresh();await b.session.command('createRecipe',null,recipeData);assert.deepEqual(writes(b)[0],{method:'post',url:'/management/recipes',body:recipeData});
});
test('Công thức chặn trùng nguyên liệu, nguyên liệu inactive, thiếu dòng, 0 và 4 số lẻ',async()=>{
    for(const ingredients of [[],[{ingredientId:1,quantity:0}],[{ingredientId:1,quantity:0.0001}],[{ingredientId:2,quantity:1}],[{ingredientId:1,quantity:1},{ingredientId:1,quantity:2}]]){
        const b=setup();await b.session.refresh();await assert.rejects(b.session.command('createRecipe',null,{...recipeData,ingredients}),e=>e.status===400);assert.equal(writes(b).length,0);
    }
});
test('Một size chỉ có một bản active, size inactive không tạo mới',async()=>{
    const b=setup();await b.session.refresh();await assert.rejects(b.session.command('createRecipe',null,{...recipeData,sizeId:11}),e=>e.status===409);await b.session.refresh();await assert.rejects(b.session.command('createRecipe',null,{...recipeData,sizeId:13}),e=>e.status===400);assert.equal(writes(b).length,0);
});
test('PUT 204 đọc lại bản mới và giữ bản cũ trong lịch sử, không coi id cũ là bản hiện tại',async()=>{
    const b=setup();await b.session.refresh();const expected=recipeSnapshot(b.server.details[31]);b.server.write=()=>{
        b.server.recipes[0].isActive=false;b.server.details[31].isActive=false;
        b.server.recipes.push({...b.server.recipes[0],recipeId:40,version:'3',isActive:true});b.server.details[40]={...copy(b.server.details[31]),recipeId:40,version:'3',isActive:true};return null;
    };
    await b.session.command('updateRecipe',31,{...recipeData,sizeId:11},expected);assert.equal(b.session.activeRecipe(11).recipeId,40);assert.equal(b.session.recipes.find(r=>r.recipeId===31).isActive,false);assert.equal(b.session.pending,null);
});
test('Bản đã thay thế hoặc định lượng bị sửa không PUT lại bằng snapshot cũ',async()=>{
    const b=setup();await b.session.refresh();const expected=recipeSnapshot(b.server.details[31]);b.server.details[31].ingredients[0].quantity=0.5;
    await assert.rejects(b.session.command('updateRecipe',31,{...recipeData,sizeId:11},expected),/vừa thay đổi/);await b.session.refresh();await assert.rejects(b.session.command('updateRecipe',30,{...recipeData,sizeId:11}),/ngừng dùng/);assert.equal(writes(b).length,0);
});
test('Khôi phục chặn bản active khác và nguyên liệu ngừng dùng',async()=>{
    const b=setup();await b.session.refresh();await assert.rejects(b.session.command('recipeActive',30,{isActive:true}),/Ngừng công thức/);
    b.server.recipes[0].isActive=false;b.server.ingredients[0].isActive=false;await b.session.refresh();await assert.rejects(b.session.command('recipeActive',30,{isActive:true}),/nguyên liệu đang hoạt động/);assert.equal(writes(b).length,0);
});
test('Khôi phục hợp lệ và ngừng công thức giữ đúng PATCH/DELETE, không tự đổi state',async()=>{
    const b=setup();b.server.recipes[0].isActive=false;await b.session.refresh();await b.session.command('recipeActive',30,{isActive:true});await b.session.command('deleteRecipe',31);
    assert.deepEqual(writes(b).map(c=>[c.method,c.url]),[['patch','/management/recipes/30/active'],['delete','/management/recipes/31']]);
});
test('Bấm đôi chỉ gửi một lệnh tạo',async()=>{
    const b=setup();await b.session.refresh();const first=b.session.command('createRecipe',null,recipeData);await assert.rejects(b.session.command('createRecipe',null,recipeData),e=>e.status===409);await first;assert.equal(writes(b).length,1);
});
test('Mất ACK sau POST giữ khóa qua reload, review chỉ đọc và cần ACK thủ công',async()=>{
    const b=setup();await b.session.refresh();b.server.write=()=>{throw Object.assign(new Error('Mất phản hồi'),{status:0});};await assert.rejects(b.session.command('createRecipe',null,recipeData));
    const restored=new CatalogSession({api:b.api,auth:b.auth,storage:b.saved});await restored.refresh();await assert.rejects(restored.command('createRecipe',null,recipeData));assert.equal(await restored.reviewPending(),false);restored.acknowledgeReview();assert.equal(restored.pending,null);assert.equal(writes(b).length,1);
});
test('Có ACK nhưng GET danh mục lỗi: đọc lại để mở khóa, không tạo lần hai',async()=>{
    const b=setup();await b.session.refresh();const original=b.api.get;let failReads=false;b.server.write=()=>{failReads=true;return {recipeId:40};};b.api.get=url=>failReads?Promise.reject(Object.assign(new Error('GET lỗi'),{status:0})):original(url);
    await assert.rejects(b.session.command('createRecipe',null,recipeData));assert.equal(b.session.pending.accepted,true);failReads=false;
    const restored=new CatalogSession({api:b.api,auth:b.auth,storage:b.saved});assert.equal(await restored.reviewPending(),true);assert.equal(restored.pending,null);assert.equal(writes(b).length,1);
});
test('Lỗi 409 tên trùng giữ message và bỏ pending; lỗi 500 giữ pending để đối chiếu',async()=>{
    for(const status of [409,500]){const b=setup();await b.session.refresh();b.server.write=()=>{throw Object.assign(new Error('Ingredient already exists'),{status});};await assert.rejects(b.session.command('createIngredient',null,{name:'Gạo',unit:'kg',minimumStock:0}),/Ingredient already exists/);assert.equal(!!b.session.pending,status===500);}
});
test('Không ACK khi chưa đối chiếu hoặc GET đối chiếu bị lỗi; pending user khác không được dùng',async()=>{
    const b=setup();await b.session.refresh();b.server.write=()=>{throw Object.assign(new Error('Lost'),{status:0});};await assert.rejects(b.session.command('deleteIngredient',1));assert.throws(()=>b.session.acknowledgeReview());b.api.get=async()=>{throw new Error('GET failed');};await assert.rejects(b.session.reviewPending());assert.throws(()=>b.session.acknowledgeReview());
    b.auth.userId=10;const other=new CatalogSession({api:b.api,auth:b.auth,storage:b.saved});assert.equal(other.pending,null);
});
test('GET chi tiết cũ về muộn không thay bản đang xem',async()=>{
    const b=setup();await b.session.refresh();let finish;const original=b.api.get;b.api.get=url=>url==='/management/recipes/30'?new Promise(r=>{finish=r;}):original(url);
    const first=b.session.selectRecipe(30);await b.session.selectRecipe(31);finish(copy(b.server.details[30]));assert.equal(await first,null);assert.equal(b.session.detail.recipeId,31);assert.equal(b.session.detailLoading,false);
});
test('Không lưu được pending thì không gửi POST; mã không hợp lệ không gọi HTTP',async()=>{
    const b=setup();await b.session.refresh();b.saved.setItem=()=>{throw new Error('Storage full');};await assert.rejects(b.session.command('createRecipe',null,recipeData),/Không lưu được/);assert.equal(writes(b).length,0);assert.equal(b.session.pending,null);
    await b.session.refresh();await assert.rejects(b.session.command('createRecipe',31,recipeData),e=>e.status===400);await assert.rejects(b.session.ingredientUsage(0),e=>e.status===400);
});
