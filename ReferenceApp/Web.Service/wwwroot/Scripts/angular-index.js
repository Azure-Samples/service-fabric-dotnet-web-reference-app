(function (angular) {
    var app = angular.module("fabrikam", ['ngCookies'])
        .controller("testController", function ($scope, $window, $http, $cookies) {


            /*--------------------VARIABLES------------------------*/

            // Shopping cart for UI display only. Description, Price, Quantity.
            $scope.cart = [];
            // ItemId, Quanity. Used in HttpPost to fulfill order.
            $scope.order = [];
            $scope.cart_total = 0;

            // Local variables to keep track of data / aid in formatting for $scope variables.
            var local_cart = [];
            var cart_total_unformatted = 0;

            // Used to keep track of customer's order. Will be undefined until order is placed.
            $scope.orderCompletedCart = $cookies.getObject('orderPlaced');
            $scope.completedTotal = $cookies.get('orderTotal');

            /* -----------------------FUNCTIONS---------------------------*/

            // Called everytime index.html is loaded. Retrieves inventory items.
            $scope.initIndex = function () {

                $http.get('/fabrikam/api/store').
                then(function (data) {
                    $scope.inventory = inventory = data.data;
                }, function (data) {
                    $window.alert("Error retrieving inventory from store.");
                });

            }

            // Called when a user adds item(s) to cart.
            $scope.submit = function (row) {

                // Ensure user enters valid non-negative integer.
                while (row.Quantity != parseInt(row.Quantity, 10) || row.Quantity <= 0) {
                    row.Quantity = "";
                    row.Quantity = prompt("Please enter a non-negative quantity: ");
                    if (row.Quantity === null) {
                        $window.alert("Order canceled.");
                        return;
                    }
                }
                // Check to see if there are enough items in inventory to order quantity specified.
                if (row.Quantity > row.CustomerAvailableStock) {
                    $window.alert("Order exceeded available stock.");
                    return;
                }

                // Add to shopping cart and calculate total cost. 
                local_cart.push({ 'Description': row.Description, 'Price': row.Price, 'Quantity': row.Quantity });
                cart_total_unformatted += row.Price * row.Quantity;

                // Update number of available items in inventory. Only updating local copy of inventory, not actual backend data.
                row.CustomerAvailableStock -= row.Quantity;

                // Copy data to scope variables to show on webpage.
                $scope.cart_total = Number(cart_total_unformatted).toFixed(2);
                $scope.cart = local_cart;

                // Keep track of Id and Quantity to send back to order service.
                $scope.order.push({ 'ItemId': row.Id, 'Quantity': row.Quantity });

                // Set UI Quantity back to empty.
                row.Quantity = "";

            }

            // Called after user enters email and selects Place Order.
            // Here's where you would store customer email if you wanted to.
            $scope.placeOrder = function (customerEmail) {

                // Make sure to store orders so user can see persisted shopping cart on confirmation page. 
                $cookies.putObject('orderPlaced', local_cart);
                $cookies.put('orderTotal', $scope.cart_total);

                // Send data to Order Controller.
                var promise = $http.post('/fabrikam/api/orders', $scope.order).
                      then(function (response) {
                          // Store returned orderId.
                          //$cookies.put('orderId', response.data);
                          var orderId = response.data;

                          var path = '/fabrikam/orderconfirmation.html?orderId=';

                          var address = path.concat(orderId);

                          // Navigate to order confirmation page.
                          window.location.href = address;

                      }, function (response) {

                          $window.alert("Error sending orders.");

                      });
            }

            // Called when orderconfirmation.html is loaded and when user clicks 'Get Order Status'
            $scope.refreshStatus = function () {

                // Initialize status in case asynchronous retrieval of status takes a while.
                $scope.orderStatus = "";

                var route = '/fabrikam/api/orders/';
                var prodId = $scope.getQueryParameterByName('orderId');
                var address = route.concat(prodId);

                // Retrieve order status based on orderId.
                $http.get(address).
                    then(function (response) {
                        $scope.orderStatus = orderStatus = response.data;
                    }, function (response) {
                        $window.alert("Error retrieving order status from store.");
                    });
            }

            $scope.createInventory = function () {
                //Description
                //Price
                //Number
                //Reorder Threshold
                //Max

                var text = $scope.itemsToCreate;
                var itemArrays = text.split('\n');

                for (var i = 0; i < itemArrays.length; i++)
                {
                    var route = '/fabrikam/api/inventory/add/';
                    var propertyArray = itemArrays[i].split(',');

                    for (var x = 0; x < propertyArray.length; x++)
                    {
                        var strtemp = encodeURIComponent(propertyArray[x].trim());
                        strtemp = strtemp.concat("/");
                        route = route.concat(strtemp)
                    }

                    $http.post(route).
                        then(function (response) {
                            $scope.createResult = createResult = response.data;
                        }, function (response) {
                            $window.alert("Error sending request to Inventory Service.");
                        });
                }




            }

            $scope.getQueryParameterByName = function (name) {

                name = name.replace(/[\[]/, "\\[").replace(/[\]]/, "\\]");

                var regex = new RegExp("[\\?&]" + name + "=([^&#]*)"), results = regex.exec(location.search);

                return results === null ? "" : decodeURIComponent(results[1].replace(/\+/g, " "));
            }

        });

})(angular)